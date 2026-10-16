using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Helpers;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.DataFileDTOs;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Text;

namespace Bank.Services.Implementations;

public class DataFileService : IDataFileService
{
    private const long MaxUploadSizeBytes = 200L * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls", ".csv" };
    private readonly ILogger<DataFileService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public DataFileService(
        ILogger<DataFileService> logger,
        IUnitOfWork unit,
        AppDbContext context,
        IFileStorageService fileStorageService,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = context;
        _fileStorageService = fileStorageService;
        _backgroundJobClient = backgroundJobClient;
    }
    public async Task<ApiResponse<DataFileImportStatusDto>> GetImportStatusAsync(
    Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<DataFileImportStatusDto>.Fail("المستخدم غير موجود.");


        var dataFile = await _dbContext.DataFiles
            .AsNoTracking()
            .Where(ScopeFilter(userScope.GroupId, currentUserId, dataFileId))
            .Select(f => new DataFileImportStatusDto(f.Id, f.ImportStatus.ToString(), f.RowCount, f.ImportError))
            .FirstOrDefaultAsync(cancellationToken);

        return dataFile is null
            ? ApiResponse<DataFileImportStatusDto>.Fail("الملف غير موجود.", null, 404)
            : ApiResponse<DataFileImportStatusDto>.SuccessResponse(dataFile);
    }

    public async Task<ApiResponse<DataFilesListResponseDto>> GetDataFilesAsync(
        Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<DataFilesListResponseDto>.Fail("المستخدم غير موجود.");

        var query = _dbContext.DataFiles.AsNoTracking();

        query = userScope.GroupId.HasValue
            ? query.Where(f => f.GroupId == userScope.GroupId.Value)
            : query.Where(f => f.GroupId == null && f.OwnerId == currentUserId);

        // Query واحدة بس — الـ aggregation (totalRows/currentUserRows) بتحصل
        // على نفس الـ list اللي رجعت من الداتابيز، من غير round trips إضافية.
        var files = await query
            .OrderBy(f => f.SortOrder)
            .Select(f => new DataFileRow(
                f.Id, f.FileName, f.RowCount, f.IsActiveForFiltering, f.SortOrder,
                f.PasswordHash != null, f.Owner.FullName, f.OwnerId, f.ImportStatus.ToString(), f.CreatedAt))
            .ToListAsync(cancellationToken);

        var items = files.Select(f => new DataFileSummaryDto(
                f.Id, f.FileName, f.RowCount, f.IsActiveForFiltering, f.SortOrder,
                f.RequiresPassword, f.OwnerName, f.ImportStatus, f.CreatedAt))
            .ToList();

        var totalRows = files.Sum(f => (long)f.RowCount);
        var currentUserRows = files.Where(f => f.OwnerId == currentUserId).Sum(f => (long)f.RowCount);

        var header = new DataFilesHeaderDto(items.Count, totalRows, currentUserRows, userScope.DisplayName);
        return ApiResponse<DataFilesListResponseDto>.SuccessResponse(new DataFilesListResponseDto(header, items));
    }

    public async Task<ApiResponse<Guid>> CreateDataFileAsync(
        Guid uploaderId, CreateDataFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var validationError = FileHelper.ValidateUploadedFile(request.File, MaxUploadSizeBytes, AllowedExtensions);
        if (validationError != null)
        {
            _logger.LogWarning("Rejected data file upload: {Reason}", validationError);
            return ApiResponse<Guid>.Fail(validationError, null, 400);
        }

        var userScope = await ResolveUserScopeAsync(uploaderId, cancellationToken);
        if (userScope == null) return ApiResponse<Guid>.Fail("المستخدم غير موجود.");

        var subFolder = userScope.GroupId.HasValue
            ? $"groups/{userScope.GroupId.Value}"
            : $"users/{uploaderId}";

        await using var stream = request.File.OpenReadStream();
        var storagePath = await _fileStorageService.SaveAsync(stream, request.File.FileName, subFolder: subFolder);

        var nextSortOrder = await GetNextSortOrderAsync(userScope.GroupId, uploaderId, CancellationToken.None);

        var dataFile = new DataFile
        {
            GroupId = userScope.GroupId,
            OwnerId = uploaderId,
            FileName = request.File.FileName,
            RowCount = 0,
            IsActiveForFiltering = false,
            PasswordHash = string.IsNullOrWhiteSpace(request.Password)
                ? null
                : BCrypt.Net.BCrypt.HashPassword(request.Password),
            StoragePath = storagePath,
            ImportStatus = DataFileImportStatus.Pending,
            SortOrder = nextSortOrder
        };

        await _unit.Repository<DataFile>().AddAsync(dataFile);
        await _unit.CompleteAsync();

        _logger.LogInformation("DataFile {DataFileId} created (groupId={GroupId}) by user {UserId}; queuing import job",
            dataFile.Id, userScope.GroupId, uploaderId);

        _backgroundJobClient.Enqueue<DataFileImportJob>(job =>
            job.ImportAsync(dataFile.Id, uploaderId, storagePath, false, CancellationToken.None));

        return ApiResponse<Guid>.SuccessResponse(dataFile.Id, "الملف اتقبل وبدأ يتعالج في الخلفية.", 201);
    }

    public async Task<ApiResponse<bool>> AppendAsync(
        Guid dataFileId, Guid currentUserId, AppendDataFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var validationError = FileHelper.ValidateUploadedFile(request.File, MaxUploadSizeBytes, AllowedExtensions);
        if (validationError is not null)
        {
            return ApiResponse<bool>.Fail(validationError);
        }

        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var dataFile = await _dbContext.DataFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, dataFileId), cancellationToken);

        if (dataFile is null)
        {
            _logger.LogWarning("Append attempted on missing DataFile {DataFileId}", dataFileId);
            return ApiResponse<bool>.Fail("الملف غير موجود.");
        }

        var appendFolder = userScope.GroupId.HasValue
            ? $"groups/{userScope.GroupId.Value}/appends/{dataFileId}"
            : $"users/{currentUserId}/appends/{dataFileId}";

        await using var stream = request.File.OpenReadStream();
        var appendStoragePath = await _fileStorageService.SaveAsync(
            stream, request.File.FileName, appendFolder, cancellationToken);

        _logger.LogInformation("Append file stored for DataFile {DataFileId} by user {UserId}; queuing import job",
            dataFileId, currentUserId);

        _backgroundJobClient.Enqueue<DataFileImportJob>(job =>
            job.ImportAsync(dataFileId, currentUserId, appendStoragePath, true, CancellationToken.None));

        return ApiResponse<bool>.SuccessResponse(true, "جاري إلحاق البيانات في الخلفية.");
    }

    public async Task<ApiResponse<bool>> ToggleActiveAsync(
        Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var affected = await _dbContext.DataFiles
            .Where(ScopeFilter(userScope.GroupId, currentUserId, dataFileId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.IsActiveForFiltering, f => !f.IsActiveForFiltering),
                cancellationToken);

        if (affected == 0)
        {
            _logger.LogWarning("ToggleActive: DataFile {DataFileId} not found", dataFileId);
            return ApiResponse<bool>.Fail("الملف غير موجود.");
        }

        _logger.LogInformation("DataFile {DataFileId} filtering toggled", dataFileId);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<bool>> ReorderAsync(
        Guid currentUserId, ReorderDataFilesRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
        {
            return ApiResponse<bool>.Fail("لا يوجد عناصر لإعادة ترتيبها.");
        }

        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        // UPDATE واحد بـ CASE WHEN بدل ما نعمل round trip منفصل لكل عنصر —
        // نفس النتيجة النهائية، لكن query واحدة بس مهما كان عدد الملفات
        // اللي اتسحبت وترتيبها اتغيّر.
        var sql = new StringBuilder("UPDATE data_files SET sort_order = CASE id ");
        var parameters = new List<object>();

        for (var i = 0; i < request.Items.Count; i++)
        {
            sql.Append($"WHEN @id{i} THEN @order{i} ");
            parameters.Add(new NpgsqlParameter($"@id{i}", request.Items[i].Id));
            parameters.Add(new NpgsqlParameter($"@order{i}", request.Items[i].SortOrder));
        }

        sql.Append("ELSE sort_order END WHERE id IN (")
           .Append(string.Join(", ", Enumerable.Range(0, request.Items.Count).Select(i => $"@id{i}")))
           .Append(") AND ");

        if (userScope.GroupId.HasValue)
        {
            sql.Append("group_id = @scopeGroupId");
            parameters.Add(new NpgsqlParameter("@scopeGroupId", userScope.GroupId.Value));
        }
        else
        {
            sql.Append("group_id IS NULL AND owner_id = @scopeUserId");
            parameters.Add(new NpgsqlParameter("@scopeUserId", currentUserId));
        }

        var affected = await _dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, cancellationToken);

        _logger.LogInformation(
            "Reordered {Requested} data files ({Affected} rows actually matched scope) for user {UserId} in a single round trip",
            request.Items.Count, affected, currentUserId);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(
        Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {

        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null)
        {
            return ApiResponse<bool>.Fail("المستخدم غير موجود.", null, StatusCodes.Status404NotFound);
        }

        var query = _dbContext.DataFiles.AsQueryable();

        query = userScope.Role switch
        {
            UserRole.SuperAdmin => query.Where(f => f.Id == dataFileId),
            UserRole.Admin when userScope.GroupId.HasValue => query.Where(f => f.Id == dataFileId && f.GroupId == userScope.GroupId.Value),
            _ => query.Where(ScopeFilter(userScope.GroupId, currentUserId, dataFileId))
        };

        var dataFile = await query.FirstOrDefaultAsync(cancellationToken);

        if (dataFile is null)
        {
            _logger.LogWarning("Delete attempted on missing DataFile {DataFileId} by user {UserId}", dataFileId, currentUserId);
            return ApiResponse<bool>.Fail("الملف غير موجود.", null, StatusCodes.Status404NotFound);
        }

        dataFile.IsActiveForFiltering = false;
        dataFile.ImportStatus = DataFileImportStatus.Processing;
        await _unit.CompleteAsync();

        _logger.LogInformation("DataFile {DataFileId} marked for deletion; queuing background cleanup", dataFileId);

        _backgroundJobClient.Enqueue<DataFileImportJob>(job =>
            job.DeleteAsync(dataFileId, dataFile.StoragePath, CancellationToken.None));

        return ApiResponse<bool>.SuccessResponse(true, "جاري حذف الملف والبيانات المرتبطة به في الخلفية.");
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> DownloadAsync(
        Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var dataFile = await _dbContext.DataFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, dataFileId), cancellationToken);

        if (dataFile is null)
        {
            _logger.LogWarning("Download attempted on missing DataFile {DataFileId}", dataFileId);
            throw new KeyNotFoundException("الملف غير موجود.");
        }

        var stream = await _fileStorageService.OpenReadAsync(dataFile.StoragePath, cancellationToken);
        _logger.LogInformation("DataFile {DataFileId} downloaded", dataFileId);

        return (stream, "application/octet-stream", dataFile.FileName);
    }

    public async Task<ApiResponse<UpdateHistoricalPlateResponseDto>> UpdateHistoricalPlateAsync(
        long historicalPlateId, Guid currentUserId, UpdateHistoricalPlateDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.RawPlate))
        {
            return ApiResponse<UpdateHistoricalPlateResponseDto>.Fail("رقم اللوحة مطلوب.", null, 400);
        }

        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<UpdateHistoricalPlateResponseDto>.Fail("المستخدم غير موجود.");

        var plate = await _dbContext.HistoricalPlates
            .Include(h => h.DataFile)
            .FirstOrDefaultAsync(h => h.Id == historicalPlateId, cancellationToken);

        if (plate is null)
        {
            return ApiResponse<UpdateHistoricalPlateResponseDto>.Fail("اللوحة غير موجودة.", null, 404);
        }

        var inScope = userScope.GroupId.HasValue
            ? plate.DataFile.GroupId == userScope.GroupId.Value
            : plate.DataFile.GroupId == null && plate.DataFile.OwnerId == currentUserId;

        if (!inScope)
        {
            _logger.LogWarning(
                "Update attempted on out-of-scope HistoricalPlate {HistoricalPlateId} by user {UserId}",
                historicalPlateId, currentUserId);
            return ApiResponse<UpdateHistoricalPlateResponseDto>.Fail("اللوحة غير موجودة.", null, 404);
        }

        var normalized = PlateNormalizer.Normalize(dto.RawPlate);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // تعديل حقيقي على الصف نفسه في historical_plates.
            plate.RawPlate = dto.RawPlate.Trim();
            plate.NormalizedPlate = normalized;
            plate.VehicleType = dto.VehicleType;
            plate.District = dto.District;
            plate.Street = dto.Street;
            plate.Location = dto.Location;
            plate.RecordedAt = dto.RecordedAt;
            plate.Notes = dto.Notes;
            plate.RecorderName = dto.RecorderName;

            // مزامنة أي match_results مرتبطة بنفس الصف (Found* fields بس) —
            // من غير عمل عملية فرز جديدة أو تقييم ضد referral_plates.
            var syncedCount = await _dbContext.MatchResults
                .Where(m => m.HistoricalPlateId == historicalPlateId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.NormalizedPlate, normalized)
                    .SetProperty(m => m.FoundVehicleType, dto.VehicleType)
                    .SetProperty(m => m.FoundDistrict, dto.District)
                    .SetProperty(m => m.FoundStreet, dto.Street)
                    .SetProperty(m => m.Location, dto.Location)
                    .SetProperty(m => m.FoundRecordedAt, dto.RecordedAt),
                    cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "HistoricalPlate {HistoricalPlateId} updated by user {UserId}; {Count} match_results rows synced (no new matching performed)",
                historicalPlateId, currentUserId, syncedCount);

            return ApiResponse<UpdateHistoricalPlateResponseDto>.SuccessResponse(
                new UpdateHistoricalPlateResponseDto(historicalPlateId, normalized, syncedCount));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<UserScopeInfo?> ResolveUserScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserScopeInfo(
                u.GroupId,
                u.Group != null ? u.Group.Name : "ملفاتي الشخصية",
                u.Role))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static Expression<Func<DataFile, bool>> ScopeFilter(Guid? groupId, Guid currentUserId, Guid dataFileId)
    {
        return groupId.HasValue
            ? f => f.Id == dataFileId && f.GroupId == groupId.Value
            : f => f.Id == dataFileId && f.GroupId == null && f.OwnerId == currentUserId;
    }

    private async Task<int> GetNextSortOrderAsync(Guid? groupId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var query = _dbContext.DataFiles.AsNoTracking();

        query = groupId.HasValue
            ? query.Where(f => f.GroupId == groupId.Value)
            : query.Where(f => f.GroupId == null && f.OwnerId == currentUserId);

        var maxOrder = await query
            .Select(f => (int?)f.SortOrder)
            .MaxAsync(cancellationToken);

        return (maxOrder ?? 0) + 1;
    }


    private record UserScopeInfo(Guid? GroupId, string DisplayName, UserRole Role);

    private record DataFileRow(
        Guid Id, string FileName, int RowCount, bool IsActiveForFiltering, int SortOrder,
        bool RequiresPassword, string OwnerName, Guid OwnerId, string ImportStatus, DateTime CreatedAt);
}
