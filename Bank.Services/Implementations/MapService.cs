using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Helpers;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.MapDTOs;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bank.Services.Implementations;

public class MapService : IMapService
{
    private const long MaxUploadSizeBytes = 20L * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls", ".csv" };

    private readonly ILogger<MapService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;
    private readonly IPlateColumnDetector _columnDetector;
    private readonly IHttpClientFactory _httpClientFactory;

    public MapService(
        ILogger<MapService> logger, IUnitOfWork unit, AppDbContext dbContext,
        IPlateColumnDetector columnDetector, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
        _columnDetector = columnDetector;
        _httpClientFactory = httpClientFactory;
    }

    // ---------- الأحدث ----------

    public async Task<ApiResponse<MapResultDto>> GetLatestAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<MapResultDto>.Fail("المستخدم غير موجود.");

        var latestJob = await _dbContext.ReferralJobs
            .AsNoTracking()
            .Where(j => j.Status == JobStatus.Completed)
            .Where(j => userScope.GroupId.HasValue
                ? j.GroupId == userScope.GroupId.Value
                : j.GroupId == null && j.UserId == currentUserId)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestJob is null)
        {
            return ApiResponse<MapResultDto>.Fail("مفيش أي عملية فرز مكتملة لسه.", null, 404);
        }

        var cached = await _dbContext.MapResults
            .Where(m => userScope.GroupId.HasValue
                ? m.GroupId == userScope.GroupId.Value
                : m.GroupId == null && m.UserId == currentUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (cached is not null && cached.ReferralJobId == latestJob.Id)
        {
            return ApiResponse<MapResultDto>.SuccessResponse(DeserializeMapResult(cached));
        }

        try
        {
            var prepared = await PrepareMapResultFromReferralJobAsync(latestJob, cancellationToken);
            await SaveMapResultAsync(userScope.GroupId, currentUserId, latestJob.Id, prepared, cancellationToken);

            return ApiResponse<MapResultDto>.SuccessResponse(prepared);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed preparing MapResult for ReferralJob {JobId}; falling back to last cached version if any",
                latestJob.Id);

            if (cached is not null)
            {
                return ApiResponse<MapResultDto>.SuccessResponse(DeserializeMapResult(cached));
            }

            return ApiResponse<MapResultDto>.Fail("تعذّر تجهيز نتيجة الخريطة دلوقتي، حاول تاني بعد شوية.", null, 500);
        }
    }

    private async Task<MapResultDto> PrepareMapResultFromReferralJobAsync(ReferralJob job, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.MatchResults
            .AsNoTracking()
            .Where(m => m.ReferralJobId == job.Id)
            .Select(m => new
            {
                m.NormalizedPlate,
                m.FoundVehicleType,
                m.FoundDistrict,
                m.FoundStreet,
                m.Location,
                m.FoundRecordedAt,
                RecorderName = m.HistoricalPlate.RecorderName,
                Notes = m.HistoricalPlate.Notes
            })
            .ToListAsync(cancellationToken);

        var httpClient = _httpClientFactory.CreateClient("MapsLinkResolver");

        // نحل كل لينك فريد مرة واحدة بس، حتى لو عشرات الصفوف بتشير لنفس اللينك.
        var coordsByLink = await LocationLinkHelper.ExtractCoordinatesBatchAsync(
            rows.Select(r => r.Location), httpClient, cancellationToken);

        var rowDtos = rows.Select(r =>
        {
            (double Latitude, double Longitude)? coords = null;
            if (!string.IsNullOrWhiteSpace(r.Location))
            {
                coordsByLink.TryGetValue(r.Location, out coords);
            }

            return new MapResultRowDto(
                r.NormalizedPlate, r.FoundVehicleType, r.FoundDistrict, r.FoundStreet,
                r.Notes, r.FoundRecordedAt, r.RecorderName, r.Location, coords?.Latitude, coords?.Longitude);
        }).ToList();

        var carCount = rowDtos.Count(r => r.Latitude.HasValue && r.Longitude.HasValue);
        var unresolvedCount = rowDtos.Count - carCount;

        return new MapResultDto(job.Id, carCount, unresolvedCount, DateTime.UtcNow, rowDtos);
    }

    private static MapResultDto DeserializeMapResult(MapResult cached)
    {
        var rows = JsonSerializer.Deserialize<List<MapResultRowDto>>(cached.ResultJson) ?? new List<MapResultRowDto>();
        return new MapResultDto(cached.ReferralJobId, cached.CarCount, cached.UnresolvedCount, cached.PreparedAt, rows);
    }

    private async Task SaveMapResultAsync(
        Guid? groupId, Guid currentUserId, Guid referralJobId, MapResultDto prepared, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.MapResults
            .Where(m => groupId.HasValue ? m.GroupId == groupId.Value : m.GroupId == null && m.UserId == currentUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            existing = new MapResult { GroupId = groupId, UserId = currentUserId };
          //  await _unit.Repository<MapResult>().AddAsync(existing);
          await _dbContext.MapResults.AddAsync(existing, cancellationToken);
        }

        existing.ReferralJobId = referralJobId;
        existing.ResultJson = JsonSerializer.Serialize(prepared.Rows);
        existing.CarCount = prepared.CarCount;
        existing.UnresolvedCount = prepared.UnresolvedCount;
        existing.PreparedAt = prepared.PreparedAt;

        await _unit.CompleteAsync();

        _logger.LogInformation(
            "MapResult refreshed for workspace (GroupId={GroupId}, UserId={UserId}) from ReferralJob {JobId}: {CarCount} cars, {UnresolvedCount} unresolved",
            groupId, currentUserId, referralJobId, prepared.CarCount, prepared.UnresolvedCount);
    }

    // ---------- الملف المرفوع ----------

    public async Task<ApiResponse<MapUploadedFileDto>> GetUploadedFileAsync(
        Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<MapUploadedFileDto>.Fail("المستخدم غير موجود.");

        var file = await _dbContext.MapUploadedFiles
            .AsNoTracking()
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == currentUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null) return ApiResponse<MapUploadedFileDto>.Fail("مفيش ملف مرفوع لسه.", null, 404);

        var rows = await _dbContext.MapUploadedPlateRows
            .AsNoTracking()
            .Where(r => r.MapUploadedFileId == file.Id)
            .ToListAsync(cancellationToken);

        return ApiResponse<MapUploadedFileDto>.SuccessResponse(BuildUploadedFileDto(file, rows));
    }

    public async Task<ApiResponse<MapUploadedFileDto>> UploadFileAsync(
        Guid currentUserId, UploadMapFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<MapUploadedFileDto>.Fail("المستخدم غير موجود.");

        if (userScope.GroupId.HasValue && userScope.Role != UserRole.Admin && userScope.Role != UserRole.SuperAdmin)
        {
            return ApiResponse<MapUploadedFileDto>.Fail("رفع/استبدال ملف الخريطة متاح لأدمن الجروب بس.", null, 403);
        }

        var validationError = ValidateUploadedFile(request.File);
        if (validationError is not null) return ApiResponse<MapUploadedFileDto>.Fail(validationError, null, 400);

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        var sheetIndex = request.SheetIndex ?? 0;

        List<ExcelSheetInfo> sheetsInfo;
        await using (var inspectStream = request.File.OpenReadStream())
        {
            sheetsInfo = ExcelSheetReader.GetSheetsInfo(inspectStream, extension);
        }

        var sheetInfo = sheetsInfo.FirstOrDefault(s => s.Index == sheetIndex) ?? sheetsInfo.FirstOrDefault();
        if (sheetInfo is null) return ApiResponse<MapUploadedFileDto>.Fail("الملف فاضي.", null, 400);

        List<ExcelSheetData> sheets;
        await using (var readStream = request.File.OpenReadStream())
        {
            sheets = ExcelSheetReader.ReadSelectedSheets(readStream, extension, allSheets: false, new List<int> { sheetInfo.Index });
        }

        var sheet = sheets.FirstOrDefault();
        if (sheet is null || sheet.Rows.Count == 0) return ApiResponse<MapUploadedFileDto>.Fail("الملف فاضي.", null, 400);

        var headerRowIdx = sheetInfo.HeaderRowIndex;
        if (headerRowIdx < 0 || headerRowIdx >= sheet.Rows.Count)
        {
            return ApiResponse<MapUploadedFileDto>.Fail("مقدرناش نحدد صف الهيدر في الملف ده.", null, 422);
        }

        var headerRow = sheet.Rows[headerRowIdx];
        var plateColumn = _columnDetector.DetectPlateColumn(headerRow, sheet.Rows.Skip(headerRowIdx + 1).Take(5));
        if (plateColumn is null)
        {
            return ApiResponse<MapUploadedFileDto>.Fail("مقدرناش نحدد عمود رقم اللوحة في الملف ده.", null, 422);
        }

        var cols = DetectFixedColumns(headerRow, plateColumn.Value);
        var dataRows = sheet.Rows.Skip(headerRowIdx + 1);

        var rawRows = new List<RawMapRow>();
        foreach (var row in dataRows)
        {
            if (cols.PlateCol >= row.Count) continue;
            var rawPlate = row[cols.PlateCol]?.Trim();
            if (string.IsNullOrWhiteSpace(rawPlate)) continue;

            string? Get(int idx) => idx != -1 && idx < row.Count ? row[idx]?.Trim() : null;

            var fixedCols = new HashSet<int>
            {
                cols.PlateCol, cols.VehicleTypeCol, cols.DistrictCol, cols.StreetCol,
                cols.GpsCol, cols.RecordedAtCol, cols.NotesCol, cols.RecorderNameCol
            };

            Dictionary<string, string?>? extra = null;
            for (var i = 0; i < headerRow.Count && i < row.Count; i++)
            {
                if (fixedCols.Contains(i)) continue;
                var header = headerRow[i]?.Trim();
                var value = row[i]?.Trim();
                if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(value)) continue;

                extra ??= new Dictionary<string, string?>();
                extra[header] = value;
            }

            rawRows.Add(new RawMapRow(
                rawPlate, Get(cols.VehicleTypeCol), Get(cols.DistrictCol), Get(cols.StreetCol),
                Get(cols.NotesCol), Get(cols.RecorderNameCol), Get(cols.GpsCol), Get(cols.RecordedAtCol),
                extra is null ? null : JsonSerializer.Serialize(extra)));
        }

        if (rawRows.Count == 0)
        {
            return ApiResponse<MapUploadedFileDto>.Fail("مفيش أي لوحات صالحة في الملف.", null, 422);
        }

        var httpClient = _httpClientFactory.CreateClient("MapsLinkResolver");
        var coordsByLink = await LocationLinkHelper.ExtractCoordinatesBatchAsync(
            rawRows.Select(r => r.Location), httpClient, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existingFile = await _dbContext.MapUploadedFiles
                .Where(f => userScope.GroupId.HasValue
                    ? f.GroupId == userScope.GroupId.Value
                    : f.GroupId == null && f.UploadedBy == currentUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingFile is not null)
            {
                await _dbContext.MapUploadedPlateRows
                    .Where(r => r.MapUploadedFileId == existingFile.Id)
                    .ExecuteDeleteAsync(cancellationToken);

                await _dbContext.MapUploadedFiles
                    .Where(f => f.Id == existingFile.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var newFile = new MapUploadedFile
            {
                GroupId = userScope.GroupId,
                UploadedBy = currentUserId,
                FileName = request.File.FileName,
                RowCount = rawRows.Count,
                UploadedAt = DateTime.UtcNow
            };

            await _unit.Repository<MapUploadedFile>().AddAsync(newFile);
            await _unit.CompleteAsync();

            var entities = rawRows.Select(r =>
            {
                (double Latitude, double Longitude)? coords = null;
                if (!string.IsNullOrWhiteSpace(r.Location))
                {
                    coordsByLink.TryGetValue(r.Location, out coords);
                }

                return new MapUploadedPlateRow
                {
                    MapUploadedFileId = newFile.Id,
                    RawPlate = r.RawPlate,
                    NormalizedPlate = PlateNormalizer.Normalize(r.RawPlate),
                    VehicleType = r.VehicleType,
                    District = r.District,
                    Street = r.Street,
                    Notes = r.Notes,
                    RecorderName = r.RecorderName,
                    LocationLink = r.Location,
                    Latitude = coords?.Latitude,
                    Longitude = coords?.Longitude,
                    RecordedAt = r.RecordedAt,
                    ExtraDataJson = r.ExtraJson
                };
            }).ToList();

            await _dbContext.MapUploadedPlateRows.AddRangeAsync(entities, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Map file '{FileName}' uploaded ({Count} rows) by user {UserId} (workspace GroupId={GroupId})",
                request.File.FileName, entities.Count, currentUserId, userScope.GroupId);

            return ApiResponse<MapUploadedFileDto>.SuccessResponse(
                BuildUploadedFileDto(newFile, entities), "تم رفع ملف الخريطة.", 201);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ApiResponse<bool>> DeleteUploadedFileAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        if (userScope.GroupId.HasValue && userScope.Role != UserRole.Admin && userScope.Role != UserRole.SuperAdmin)
        {
            return ApiResponse<bool>.Fail("حذف ملف الخريطة متاح لأدمن الجروب بس.", null, 403);
        }

        var file = await _dbContext.MapUploadedFiles
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == currentUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null) return ApiResponse<bool>.Fail("مفيش ملف مرفوع أصلاً.", null, 404);

        await _dbContext.MapUploadedPlateRows.Where(r => r.MapUploadedFileId == file.Id).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.MapUploadedFiles.Where(f => f.Id == file.Id).ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Map uploaded file {FileId} deleted by user {UserId}", file.Id, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    private static MapUploadedFileDto BuildUploadedFileDto(MapUploadedFile file, List<MapUploadedPlateRow> rows)
    {
        var carCount = rows.Count(r => r.Latitude.HasValue && r.Longitude.HasValue);
        var unresolvedCount = rows.Count - carCount;

        var rowDtos = rows.Select(r => new MapUploadedFileRowDto(
            r.Id, r.NormalizedPlate, r.VehicleType, r.District, r.Street, r.Notes, r.RecordedAt,
            r.RecorderName, r.LocationLink, r.Latitude, r.Longitude)).ToList();

        return new MapUploadedFileDto(file.Id, file.FileName, file.RowCount, carCount, unresolvedCount, file.UploadedAt, rowDtos);
    }

    // نفس كلمات AssignColumn في DataFileImportJob بالظبط — عشان نفس أسماء
    // الأعمدة في كل حتة في النظام.
    private static FixedColumnMap DetectFixedColumns(List<string?> headerRow, int plateColumn)
    {
        var map = new FixedColumnMap { PlateCol = plateColumn };

        for (var i = 0; i < headerRow.Count; i++)
        {
            if (i == plateColumn) continue;

            var header = headerRow[i]?.Trim() ?? string.Empty;
            if (header.Length == 0) continue;

            if (header.Contains("ملاحظ")) map.NotesCol = i;
            else if (header.Contains("المسجل")) map.RecorderNameCol = i;
            else if (header.Contains("نوع") || header.Contains("السيارة")) map.VehicleTypeCol = i;
            else if (header.Contains("الشارع")) map.StreetCol = i;
            else if (header.Contains("الحي")) map.DistrictCol = i;
            else if (header.Contains("GPS") || header.Contains("الموقع") || header.Contains("رابط")) map.GpsCol = i;
            else if (header.Contains("تاريخ") || header.Contains("التسجيل")) map.RecordedAtCol = i;
        }

        return map;
    }

    private static string? ValidateUploadedFile(Microsoft.AspNetCore.Http.IFormFile? file)
    {
        if (file is null || file.Length == 0) return "لازم ترفع ملف.";
        if (file.Length > MaxUploadSizeBytes) return $"حجم الملف أكبر من الحد المسموح ({MaxUploadSizeBytes / 1024 / 1024} MB).";

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return $"صيغة الملف غير مدعومة. المسموح: {string.Join(", ", AllowedExtensions)}";
        }

        return null;
    }

    private async Task<UserScopeInfo?> ResolveUserScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserScopeInfo(u.GroupId, u.Role))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record RawMapRow(
        string RawPlate, string? VehicleType, string? District, string? Street,
        string? Notes, string? RecorderName, string? Location, string? RecordedAt, string? ExtraJson);

    private sealed class FixedColumnMap
    {
        public int PlateCol { get; set; } = -1;
        public int VehicleTypeCol { get; set; } = -1;
        public int DistrictCol { get; set; } = -1;
        public int StreetCol { get; set; } = -1;
        public int GpsCol { get; set; } = -1;
        public int RecordedAtCol { get; set; } = -1;
        public int NotesCol { get; set; } = -1;
        public int RecorderNameCol { get; set; } = -1;
    }

    private sealed record UserScopeInfo(Guid? GroupId, UserRole Role);
}
