using System.Data;
using System.Globalization;
using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CheckFileDTos;
using Bank.Shared.Enums;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using Npgsql;
using NpgsqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Bank.Services.Implementations;

public class CheckFileService : ICheckFileService
{
    private const long MaxUploadSizeBytes = 50L * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls", ".csv" };

    private const int PlateColumnIndex = 0;
    private const int VehicleTypeColumnIndex = 1;
    private const int BankNameColumnIndex = 2;
    private const int ChassisNumberColumnIndex = 3;

    private readonly ILogger<CheckFileService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;
    private readonly ICheckPlateCacheService _cache;
    private readonly IConfiguration _configuration;

    public CheckFileService(
        ILogger<CheckFileService> logger, IUnitOfWork unit, AppDbContext dbContext, ICheckPlateCacheService cache
        , IConfiguration configuration)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
        _cache = cache;
        _configuration = configuration;
    }

    public async Task<ApiResponse<List<CheckFileSummaryDto>>> GetCheckFilesAsync(
        Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<List<CheckFileSummaryDto>>.Fail("المستخدم غير موجود.");

        var query = _dbContext.CheckFiles.AsNoTracking();
        query = userScope.GroupId.HasValue
            ? query.Where(f => f.GroupId == userScope.GroupId.Value)
            : query.Where(f => f.GroupId == null && f.UploadedBy == currentUserId);

        var files = await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new CheckFileSummaryDto(f.Id, f.FileName, f.TotalPlates, f.IsActive, f.Uploader.FullName, f.CreatedAt))
            .ToListAsync(cancellationToken);

        return ApiResponse<List<CheckFileSummaryDto>>.SuccessResponse(files);
    }

    public async Task<ApiResponse<CheckFileSummaryDto>> UploadAsync(
        Guid uploaderId, CreateCheckFileRequestDto request, CancellationToken cancellationToken = default)
    {
        //var canUploadDataFilePermission=await _dbContext.UserPermissions
        //    .AnyAsync(p => p.UserId == uploaderId && p.CanUploadDataFiles , cancellationToken);
        //if(!canUploadDataFilePermission)
        //{
        //    return ApiResponse<CheckFileSummaryDto>.Fail("ليس لديك صلاحية رفع ملفات التشيك.");
        //}
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var validationError = ValidateUploadedFile(request.File);
        if (validationError is not null)
        {
            _logger.LogWarning("Rejected check file upload: {Reason}", validationError);
            return ApiResponse<CheckFileSummaryDto>.Fail(validationError, null, 400);
        }

        var userScope = await ResolveUserScopeAsync(uploaderId, cancellationToken);
        if (userScope == null) return ApiResponse<CheckFileSummaryDto>.Fail("المستخدم غير موجود.");

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        List<List<string?>> allRows;
        await using (var stream = request.File.OpenReadStream())
        {
            allRows = ReadAllRowsAsStrings(stream, extension).ToList();
        }

        if (allRows.Count < 2)
        {
            return ApiResponse<CheckFileSummaryDto>.Fail("الملف فارغ أو فيه صف العناوين بس.", null, 400);
        }

        var dataRows = allRows.Skip(1).ToList();

        await DeactivateExistingActiveFileAsync(userScope.GroupId, uploaderId, cancellationToken);

        var checkFile = new CheckFile
        {
            GroupId = userScope.GroupId,
            UploadedBy = uploaderId,
            FileName = request.File.FileName,
            TotalPlates = 0,
            IsActive = true
        };

        await _unit.Repository<CheckFile>().AddAsync(checkFile);
        await _unit.CompleteAsync();

        var insertedCount = await BulkInsertCheckPlatesAsync(_dbContext, checkFile.Id, dataRows, cancellationToken);

        checkFile.TotalPlates = insertedCount;
        await _unit.CompleteAsync();

        _cache.InvalidateActiveCheckFileCache(userScope.GroupId, uploaderId);

        _logger.LogInformation(
            "CheckFile {CheckFileId} uploaded by user {UserId} ({Count} plates across multiple banks)",
            checkFile.Id, uploaderId, insertedCount);
        timer.Stop();
        _logger.LogInformation("CheckFile upload processing time: {ElapsedMilliseconds} ms", timer.ElapsedMilliseconds);

        var dto = new CheckFileSummaryDto(checkFile.Id, checkFile.FileName, insertedCount, true, "أنت", checkFile.CreatedAt);
        return ApiResponse<CheckFileSummaryDto>.SuccessResponse(dto, "اتحمّل ملف التشيك بنجاح.", 201);
    }

    public async Task<ApiResponse<bool>> ToggleActiveAsync(
        Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var checkFile = await _dbContext.CheckFiles
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, checkFileId), cancellationToken);

        if (checkFile is null) return ApiResponse<bool>.Fail("الملف غير موجود.");

        if (!checkFile.IsActive)
        {
            await DeactivateExistingActiveFileAsync(userScope.GroupId, currentUserId, cancellationToken);
        }

        checkFile.IsActive = !checkFile.IsActive;
        await _unit.CompleteAsync();

        _cache.InvalidateActiveCheckFileCache(userScope.GroupId, currentUserId);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(
        Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var checkFile = await _dbContext.CheckFiles
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, checkFileId), cancellationToken);

        if (checkFile is null) return ApiResponse<bool>.Fail("الملف غير موجود.");

       await _unit.Repository<CheckFile>().RemoveAsync(checkFile);
        await _unit.CompleteAsync();

        if (checkFile.IsActive)
        {
            _cache.InvalidateActiveCheckFileCache(userScope.GroupId, currentUserId);
        }

        _logger.LogInformation("CheckFile {CheckFileId} deleted by user {UserId}", checkFileId, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    private async Task DeactivateExistingActiveFileAsync(Guid? groupId, Guid uploaderId, CancellationToken cancellationToken)
    {
        var query = _dbContext.CheckFiles.Where(f => f.IsActive);
        query = groupId.HasValue
            ? query.Where(f => f.GroupId == groupId.Value)
            : query.Where(f => f.GroupId == null && f.UploadedBy == uploaderId);

        await query.ExecuteUpdateAsync(setters => setters.SetProperty(f => f.IsActive, false), cancellationToken);
    }

    private async Task<int> BulkInsertCheckPlatesAsync(
        AppDbContext dbContext, Guid checkFileId, List<List<string?>> dataRows, CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("check_file_id", typeof(Guid));
        table.Columns.Add("raw_plate", typeof(string));
        table.Columns.Add("normalized_plate", typeof(string));
        table.Columns.Add("bank_name", typeof(string));
        table.Columns.Add("vehicle_type", typeof(string));
        table.Columns.Add("chassis_number", typeof(string));

        var seen = new HashSet<string>();

        // Query DB for already-existing normalized plates for this check file
        var existingPlates = new HashSet<string>(await dbContext.CheckPlates
            .Where(p => p.CheckFileId == checkFileId)
            .Select(p => p.NormalizedPlate)
            .ToListAsync(cancellationToken));

        foreach (var row in dataRows)
        {
            if (PlateColumnIndex >= row.Count) continue;
            var rawPlate = row[PlateColumnIndex]?.Trim();
            if (string.IsNullOrWhiteSpace(rawPlate)) continue;

            var normalized = PlateNormalizer.Normalize(rawPlate);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized)) continue;

            // Skip if this normalized plate already exists in DB for this check file
            if (existingPlates.Contains(normalized)) continue;

            var dr = table.NewRow();
            dr["check_file_id"] = checkFileId;
            dr["raw_plate"] = rawPlate;
            dr["normalized_plate"] = normalized;
            dr["bank_name"] = GetCell(row, BankNameColumnIndex) ?? "غير محدد";
            dr["vehicle_type"] = (object?)GetCell(row, VehicleTypeColumnIndex) ?? DBNull.Value;
            dr["chassis_number"] = (object?)GetCell(row, ChassisNumberColumnIndex) ?? DBNull.Value;
            table.Rows.Add(dr);
        }

        if (table.Rows.Count == 0) return 0;

        var connectionString = _configuration.GetConnectionString("DefaultConnection")
     ?? throw new InvalidOperationException("Could not resolve the database connection string.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var importer = await connection.BeginBinaryImportAsync(
            "COPY check_plates (check_file_id, raw_plate, normalized_plate, bank_name, vehicle_type, chassis_number) FROM STDIN (FORMAT BINARY)", cancellationToken);
        importer.Timeout = TimeSpan.FromSeconds(120);

        foreach (DataRow row in table.Rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync((Guid)row["check_file_id"], NpgsqlDbType.Uuid, cancellationToken);
            foreach (var column in new[] { "raw_plate", "normalized_plate", "bank_name", "vehicle_type", "chassis_number" })
            {
                if (row[column] is string value)
                    await importer.WriteAsync(value, NpgsqlDbType.Varchar, cancellationToken);
                else
                    await importer.WriteNullAsync(cancellationToken);
            }
        }

        await importer.CompleteAsync(cancellationToken);
        return table.Rows.Count;
    }

    private static string? GetCell(List<string?> row, int index) => index < row.Count ? row[index]?.Trim() : null;

    private async Task<UserScopeInfo?> ResolveUserScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserScopeInfo(u.GroupId, u.Role))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static System.Linq.Expressions.Expression<Func<CheckFile, bool>> ScopeFilter(Guid? groupId, Guid currentUserId, Guid checkFileId)
    {
        return groupId.HasValue
            ? f => f.Id == checkFileId && f.GroupId == groupId.Value
            : f => f.Id == checkFileId && f.GroupId == null && f.UploadedBy == currentUserId;
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

    /// <summary>مستخدمة كمان من ReferralJobService (نفس منطق القراءة، أعمدة مختلفة).</summary>
    internal static IEnumerable<List<string?>> ReadAllRowsAsStrings(Stream stream, string extension)
    {
        if (extension == ".csv")
        {
            using var textReader = new StreamReader(stream);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
                MissingFieldFound = null,
                BadDataFound = null
            };
            using var csv = new CsvReader(textReader, config);

            while (csv.Read())
            {
                var record = csv.Parser.Record;
                yield return record?.Select(v => (string?)v).ToList() ?? new List<string?>();
            }

            yield break;
        }

        using var reader = ExcelReaderFactory.CreateReader(stream);
        while (reader.Read())
        {
            var row = new List<string?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row.Add(reader.GetValue(i)?.ToString()?.Trim());
            }
            yield return row;
        }
    }

    private record UserScopeInfo(Guid? GroupId, UserRole Role);
}
