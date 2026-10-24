using Bank.Services.Helpers;
using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.RecordingArchiveDTOs;
using Bank.Shared.Enums;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

public class RecordingArchiveService : IRecordingArchiveService
{
    private readonly ILogger<RecordingArchiveService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IPermissionService _permissions;

    public RecordingArchiveService(
        ILogger<RecordingArchiveService> logger, IUnitOfWork unit, AppDbContext dbContext, IFileStorageService fileStorage,
        IPermissionService permissions)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _permissions = permissions;
    }
    public async Task<ApiResponse<bool>> UpdateRowAsync(
      long id, Guid currentUserId, UpdateRecordingArchiveRowDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RawPlate))
        {
            return ApiResponse<bool>.Fail("رقم اللوحة مطلوب.", null, 400);
        }

        var row = await _dbContext.RecordingArchivePlates
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUserId, cancellationToken);

        if (row is null) return ApiResponse<bool>.Fail("الصف غير موجود.", null, 404);

        row.RawPlate = request.RawPlate.Trim();
        row.NormalizedPlate = PlateNormalizer.Normalize(request.RawPlate);
        row.VehicleType = request.VehicleType;
        row.District = request.District;
        row.Street = request.Street;
        row.Notes = request.Notes;
        row.RecorderName = request.RecorderName;

        await _dbContext.SaveChangesAsync(cancellationToken);

       
        if (request.ApplyDistrictStreetToWholeSession && row.SourceSessionId.HasValue)
        {
            var sessionId = row.SourceSessionId.Value;

            var sessionUpdated = await _dbContext.RecordingSessions
                .Where(s => s.Id == sessionId && s.UserId == currentUserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.District, request.District)
                    .SetProperty(s => s.Street, request.Street),
                    cancellationToken);

            await _dbContext.RecordedPlates
                .Where(p => p.SessionId == sessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.District, request.District)
                    .SetProperty(p => p.Street, request.Street),
                    cancellationToken);

            await _dbContext.RecordingArchivePlates
                .Where(a => a.SourceSessionId == sessionId && a.UserId == currentUserId && a.Id != id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.District, request.District)
                    .SetProperty(a => a.Street, request.Street),
                    cancellationToken);

            _logger.LogInformation(
                "District/Street propagated to whole RecordingSession {SessionId} (session row matched: {SessionUpdated}) from archive row {Id} by user {UserId}",
                sessionId, sessionUpdated > 0, id, currentUserId);
        }

        _logger.LogInformation("RecordingArchivePlate {Id} updated by user {UserId}", id, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true);
    }
    public async Task ReplaceSessionEntriesAsync(
        Guid sessionId, Guid userId, List<RecordingArchiveEntryInput> entries, CancellationToken cancellationToken = default)
    {
     
        await _dbContext.RecordingArchivePlates
            .Where(a => a.SourceSessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken);

        if (entries.Count == 0) return;

        var rows = entries.Select(e => new RecordingArchivePlate
        {
            UserId = userId,
            RawPlate = e.RawPlate,
            NormalizedPlate = e.NormalizedPlate,
            VehicleType = e.VehicleType,
            District = e.District,
            Street = e.Street,
            Notes = e.Notes,
            Location = e.LocationLink,
            RecordedAt = e.RecordedAt,
            RecorderName = e.RecorderName,
            SourceSessionId = sessionId,
            IsMatched = e.IsMatched
        });

        await _dbContext.RecordingArchivePlates.AddRangeAsync(rows, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Archived {Count} plates from RecordingSession {SessionId} for user {UserId}",
            entries.Count, sessionId, userId);
    }

    public async Task<ApiResponse<RecordingArchiveListResponseDto>> GetAsync(
        Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.RecordingArchivePlates.AsNoTracking().Where(a => a.UserId == currentUserId);

        var totalCount = await query.CountAsync(cancellationToken);

        var lastUpdated = totalCount == 0
            ? DateTime.UtcNow
            : await query.MaxAsync(a => a.CreatedAt, cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new RecordingArchiveRowDto(
                a.Id, a.NormalizedPlate, a.RawPlate, a.Notes, a.VehicleType, a.District, a.Street,
                a.RecordedAt, a.Location, a.RecorderName, a.SourceSessionId, a.AppendedToDataFileId, a.IsMatched))
            .ToListAsync(cancellationToken);

        var result = new PagedResult<RecordingArchiveRowDto>
        {
            Items = items,
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        var title = $"نسخة تسجيل {lastUpdated:yyyy-MM-dd}";
        return ApiResponse<RecordingArchiveListResponseDto>.SuccessResponse(new RecordingArchiveListResponseDto(title, result));
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportAsync(
        Guid currentUserId,string? fileName, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.RecordingArchivePlates
            .AsNoTracking()
            .Where(a => a.UserId == currentUserId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.NormalizedPlate, a.Location, a.RecordedAt, a.District, a.Street, a.Notes, a.VehicleType, a.RecorderName
            })
            .ToListAsync(cancellationToken);

        // اسم الملف/الشيت بتاريخ التنزيل نفسه (مش تاريخ آخر صف)، زي ما اتفقنا.
        var title =fileName?? $"نسخة تسجيل {DateTime.UtcNow.ToSaudiLocalTime():yyyy-MM-dd}";

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(title);
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        sheet.Cell(1, 2).Value = "نوع السيارة";
        sheet.Cell(1, 3).Value = "ملاحظات";

        sheet.Cell(1, 4).Value = "GPS";
        sheet.Cell(1, 5).Value = "الشارع";
        sheet.Cell(1, 6).Value = "الحي";

        sheet.Cell(1, 7).Value = "تاريخ التسجيل";
        sheet.Cell(1, 8).Value = "اسم المسجّل";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.NormalizedPlate;
            sheet.Cell(r, 2).Value = row.VehicleType;
            sheet.Cell(r, 3).Value = row.Notes;

            sheet.Cell(r, 4).Value = row.Location;
            sheet.Cell(r, 5).Value = row.Street;
            sheet.Cell(r, 6).Value = row.District;

            sheet.Cell(r, 7).Value = row.RecordedAt.ToSaudiLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cell(r, 8).Value = row.RecorderName;
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return (stream.ToArray(), $"{title}.xlsx");
    }

    public async Task<ApiResponse<bool>> DeleteAllAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var deleted = await _dbContext.RecordingArchivePlates
            .Where(a => a.UserId == currentUserId)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Cleared {Count} recording archive rows for user {UserId}", deleted, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true, $"اتمسح {deleted} صف من الأرشيف.");
    }

    public async Task<ApiResponse<ConvertArchiveToDataFileResponseDto>> ConvertToDataFileAsync(
        Guid currentUserId, ConvertArchiveToDataFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var hasExisting = request.ExistingDataFileId.HasValue;
        var hasNewName = !string.IsNullOrWhiteSpace(request.NewDataFileName);

        if (hasExisting == hasNewName)
        {
            return ApiResponse<ConvertArchiveToDataFileResponseDto>.Fail(
                "اختار إما ملف داتا موجود أو اسم لملف جديد، مش الاتنين ولا من غيرهم.", null, 400);
        }

        var userScope = await _dbContext.Users
            .AsNoTracking()
            .WithEffectiveGroup()
            .Where(x => x.User.Id == currentUserId)
            .Select(x => new { x.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) return ApiResponse<ConvertArchiveToDataFileResponseDto>.Fail("المستخدم غير موجود.");

        var allowed = hasExisting
            ? await _permissions.CanManageDataFileAsync(currentUserId, request.ExistingDataFileId!.Value, cancellationToken)
            : await _permissions.CanUploadDataFilesAsync(currentUserId, cancellationToken);
        if (!allowed)
        {
            return ApiResponse<ConvertArchiveToDataFileResponseDto>.Fail("ليس لديك صلاحية على ملف الداتا ده.", null, 403);
        }

        // بس الصفوف اللي لسه ما اتحوّلتش لملف داتا قبل كده — التحويل تراكمي،
        // مش بيفضّي الأرشيف، فلو عملته تاني بياخد الجديد بس.
        var pendingRows = await _dbContext.RecordingArchivePlates
            .Where(a => a.UserId == currentUserId && (a.AppendedToDataFileId == null))
            .ToListAsync(cancellationToken);

        if (pendingRows.Count == 0)
        {
            return ApiResponse<ConvertArchiveToDataFileResponseDto>.Fail("مفيش صفوف جديدة في الأرشيف لسه ما اتحوّلتش.", null, 400);
        }

        Guid dataFileId;

        if (hasExisting)
        {
            // النطاق اتشيك فوق في CanManageDataFileAsync (بيشمل الجروب اللي الأدمن بيديره)،
            // فمنعيدش فلترته بـ userScope.GroupId وإلا الأدمن ميلاقيش ملف جروبه.
            var existingFile = await _dbContext.DataFiles
                .Where(f => f.Id == request.ExistingDataFileId!.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingFile is null)
            {
                return ApiResponse<ConvertArchiveToDataFileResponseDto>.Fail("ملف الداتا غير موجود.", null, 404);
            }

            dataFileId = existingFile.Id;

            await _dbContext.DataFiles
                .Where(f => f.Id == dataFileId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.RowCount, f => f.RowCount + pendingRows.Count), cancellationToken);
        }
        else
        {
            var fileName = request.NewDataFileName!.Trim();
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) fileName += ".xlsx";

            var fileBytes = BuildHistoricalWorkbook(pendingRows);
            var subFolder = userScope.GroupId.HasValue ? $"groups/{userScope.GroupId.Value}" : $"users/{currentUserId}";

            await using var memoryStream = new MemoryStream(fileBytes);
            var storagePath = await _fileStorage.SaveAsync(memoryStream, fileName, subFolder, cancellationToken);

            var nextSortOrder = await GetNextSortOrderAsync(userScope.GroupId, currentUserId, cancellationToken);

            var newDataFile = new DataFile
            {
                GroupId = userScope.GroupId,
                OwnerId = currentUserId,
                FileName = fileName,
                RowCount = pendingRows.Count,
                IsActiveForFiltering = true,
                StoragePath = storagePath,
                ImportStatus = DataFileImportStatus.Completed,
                SortOrder = nextSortOrder
            };

            await _unit.Repository<DataFile>().AddAsync(newDataFile);
            await _unit.CompleteAsync();

            dataFileId = newDataFile.Id;
        }

        var historicalPlates = pendingRows.Select(a => new HistoricalPlate
        {
            DataFileId = dataFileId,
            UserId = currentUserId,
            RawPlate = a.RawPlate,
            NormalizedPlate = a.NormalizedPlate,
            VehicleType = a.VehicleType,
            District = a.District,
            Street = a.Street,
            Notes = a.Notes,
            RecorderName = a.RecorderName,
            Location = a.Location,
            RecordedAt = a.RecordedAt.ToString("yyyy-MM-dd HH:mm:ss")
        });

        await _dbContext.HistoricalPlates.AddRangeAsync(historicalPlates, cancellationToken);

        var appendedAt = DateTime.UtcNow;
        foreach (var row in pendingRows)
        {
            row.AppendedToDataFileId = dataFileId;
            row.AppendedAt = appendedAt;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Converted {Count} recording archive rows into DataFile {DataFileId} for user {UserId}",
            pendingRows.Count, dataFileId, currentUserId);

        return ApiResponse<ConvertArchiveToDataFileResponseDto>.SuccessResponse(
            new ConvertArchiveToDataFileResponseDto(dataFileId, pendingRows.Count));
    }

    public async Task<ApiResponse<bool>> DeleteRowAsync(long id, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var deleted = await _dbContext.RecordingArchivePlates
            .Where(a => a.Id == id && a.UserId == currentUserId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0) return ApiResponse<bool>.Fail("الصف غير موجود.", null, 404);
        _logger.LogInformation("Deleted RecordingArchivePlate {Id} for user {UserId}", id, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true);
    }
    private static byte[] BuildHistoricalWorkbook(List<RecordingArchivePlate> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("الداتا");
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        sheet.Cell(1, 2).Value = "ملاحظات";
        sheet.Cell(1, 3).Value = "نوع السيارة";
        sheet.Cell(1, 4).Value = "الشارع";
        sheet.Cell(1, 5).Value = "الحي";
        sheet.Cell(1, 6).Value = "GPS";
        sheet.Cell(1, 7).Value = "تاريخ التسجيل";
        sheet.Cell(1, 8).Value = "اسم المسجّل";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.RawPlate;
            sheet.Cell(r, 2).Value = row.Notes;
            sheet.Cell(r, 3).Value = row.VehicleType;
            sheet.Cell(r, 4).Value = row.Street;
            sheet.Cell(r, 5).Value = row.District;
            sheet.Cell(r, 6).Value = row.Location;
            sheet.Cell(r, 7).Value = row.RecordedAt.ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cell(r, 8).Value = row.RecorderName;
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<int> GetNextSortOrderAsync(Guid? groupId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var query = _dbContext.DataFiles.AsNoTracking();
        query = groupId.HasValue
            ? query.Where(f => f.GroupId == groupId.Value)
            : query.Where(f => f.GroupId == null && f.OwnerId == currentUserId);

        var maxOrder = await query.Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken);
        return (maxOrder ?? 0) + 1;
    }

}
