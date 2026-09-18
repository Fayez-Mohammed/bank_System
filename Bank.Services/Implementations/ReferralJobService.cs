using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Helpers;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.ReferralJobDTOs;
using Bank.Shared.Enums;
using ClosedXML.Excel;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
namespace Bank.Services.Implementations;

public class ReferralJobService : IReferralJobService
{
    private const long MaxUploadSizeBytes = 50L * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls", ".csv" };

    private readonly ILogger<ReferralJobService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;
    private readonly IPlateColumnDetector _columnDetector;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ICheckPlateCacheService _checkPlateCache;

    public ReferralJobService(
        ILogger<ReferralJobService> logger, IUnitOfWork unit, AppDbContext dbContext,
        IPlateColumnDetector columnDetector, IBackgroundJobClient backgroundJobClient,
        ICheckPlateCacheService checkPlateCache)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
        _columnDetector = columnDetector;
        _backgroundJobClient = backgroundJobClient;
        _checkPlateCache = checkPlateCache;
    }
    public async Task<ApiResponse<InspectReferralFileResponseDto>> InspectUploadAsync(
    IFormFile file, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateUploadedFile(file);
        if (validationError is not null) return ApiResponse<InspectReferralFileResponseDto>.Fail(validationError, null, 400);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        List<ExcelSheetInfo> sheets;
        await using (var stream = file.OpenReadStream())
        {
            sheets = ExcelSheetReader.GetSheetsInfo(stream, extension);
        }

        var dto = new InspectReferralFileResponseDto(
            sheets.Select(s => new SheetInfoDto(s.Index, s.Name, s.RowCount)).ToList());

        return ApiResponse<InspectReferralFileResponseDto>.SuccessResponse(dto);
    }

    public async Task<ApiResponse<Guid>> CreateFromUploadAsync(
        Guid userId, CreateReferralJobUploadRequestDto request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateUploadedFile(request.File);
        if (validationError is not null) return ApiResponse<Guid>.Fail(validationError, null, 400);

        if (!request.AllSheets && (request.SelectedSheetIndexes is null || request.SelectedSheetIndexes.Count == 0))
        {
            return ApiResponse<Guid>.Fail("لازم تختار Sheet واحدة على الأقل، أو تفعّل \"كل الـ Sheets\".", null, 400);
        }

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();

        List<ExcelSheetData> sheets;
        await using (var stream = request.File.OpenReadStream())
        {
            sheets = ExcelSheetReader.ReadSelectedSheets(stream, extension, request.AllSheets, request.SelectedSheetIndexes);
        }

        
        var rawPlates = new List<string>();
        foreach (var sheet in sheets)
        {
            if (sheet.Rows.Count < 2) continue;

            var headerRow = sheet.Rows[0];
            var dataRows = sheet.Rows.Skip(1).ToList();
            var sample = dataRows.Take(5).ToList();

            var plateColumn = _columnDetector.DetectPlateColumn(headerRow, sample);
            if (plateColumn is null)
            {
                _logger.LogWarning("Could not detect plate column in sheet '{SheetName}' (index {Index}) — skipping this sheet", sheet.Name, sheet.Index);
                continue;  
            }

            var platesInSheet = dataRows
                .Where(r => plateColumn.Value < r.Count)
                .Select(r => r[plateColumn.Value])
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim());

            rawPlates.AddRange(platesInSheet);
        }

        if (rawPlates.Count == 0)
        {
            return ApiResponse<Guid>.Fail("مقدرناش نحدد عمود رقم اللوحة في أي Sheet من اللي اخترتها.", null, 422);
        }

       
        return await CreateReferralJobInternalAsync(
            userId, request.FilterType, SourceType.FileUpload, request.File.FileName,
            request.Password, rawPlates, cancellationToken);
    }
    //public async Task<ApiResponse<Guid>> CreateFromUploadAsync(
    //    Guid userId, CreateReferralJobUploadRequestDto request, CancellationToken cancellationToken = default)
    //{
    //    if (request.File is null || request.File.Length == 0)
    //    {
    //        return ApiResponse<Guid>.Fail("لازم ترفع ملف.", null, 400);
    //    }
    //    if (request.File.Length > MaxUploadSizeBytes)
    //    {
    //        return ApiResponse<Guid>.Fail($"حجم الملف أكبر من الحد المسموح ({MaxUploadSizeBytes / 1024 / 1024} MB).", null, 400);
    //    }
    //    var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
    //    if (!AllowedExtensions.Contains(extension))
    //    {
    //        return ApiResponse<Guid>.Fail($"صيغة الملف غير مدعومة. المسموح: {string.Join(", ", AllowedExtensions)}", null, 400);
    //    }

    //    List<List<string?>> allRows;
    //    await using (var stream = request.File.OpenReadStream())
    //    {

    //        allRows = CheckFileService.ReadAllRowsAsStrings(stream, extension).ToList();
    //    }

    //    if (allRows.Count < 2)
    //    {
    //        return ApiResponse<Guid>.Fail("الملف فارغ أو فيه صف العناوين بس.", null, 400);
    //    }

    //    var headerRow = allRows[0];
    //    var dataRows = allRows.Skip(1).ToList();
    //    var sample = dataRows.Take(5).ToList();

    //    var plateColumn = _columnDetector.DetectPlateColumn(headerRow, sample);
    //    if (plateColumn is null)
    //    {
    //        return ApiResponse<Guid>.Fail("مقدرناش نحدد عمود رقم اللوحة تلقائيًا من الملف ده.", null, 422);
    //    }

    //    var rawPlates = dataRows
    //        .Where(r => plateColumn.Value < r.Count)
    //        .Select(r => r[plateColumn.Value])
    //        .Where(v => !string.IsNullOrWhiteSpace(v))
    //        .Select(v => v!.Trim())
    //        .ToList();


    //    return await CreateReferralJobInternalAsync(
    //        userId, request.FilterType, SourceType.FileUpload, request.File.FileName,
    //        request.Password, rawPlates, cancellationToken);
    //}

    public async Task<ApiResponse<Guid>> CreateFromPasteAsync(
        Guid userId, CreateReferralJobPasteRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlatesText))
        {
            return ApiResponse<Guid>.Fail("محتاج تلصق لوحة واحدة على الأقل.", null, 400);
        }

        var rawPlates = request.PlatesText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        return await CreateReferralJobInternalAsync(
            userId, request.FilterType, SourceType.DirectPaste, null, null, rawPlates, cancellationToken);
    }

    private async Task<ApiResponse<Guid>> CreateReferralJobInternalAsync(
        Guid userId, FilterType filterType, SourceType sourceType, string? sourceFileName,
        string? password, List<string> rawPlates, CancellationToken cancellationToken)
    {
        var userScope = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) return ApiResponse<Guid>.Fail("المستخدم غير موجود.");

       
        var distinctPlates = new Dictionary<string, string>();
        foreach (var raw in rawPlates)
        {
            var normalized = PlateNormalizer.NormalizeReferralPlate(raw);
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            distinctPlates.TryAdd(normalized, raw);
        }

        if (distinctPlates.Count == 0)
        {
            return ApiResponse<Guid>.Fail("مفيش أي لوحة صالحة في الملف/النص.", null, 400);
        }

        var referralJob = new ReferralJob
        {
            GroupId = userScope.GroupId,
            UserId = userId,
            FilterType = filterType,
            SourceType = sourceType,
            SourceFileName = sourceFileName,
            PasswordHash = string.IsNullOrWhiteSpace(password) ? null : BCrypt.Net.BCrypt.HashPassword(password),
            TotalReferralPlates = distinctPlates.Count,
            Status = JobStatus.Processing
        };

        await _unit.Repository<ReferralJob>().AddAsync(referralJob);
        await _unit.CompleteAsync();

        await BulkInsertReferralPlatesAsync(referralJob.Id, distinctPlates, cancellationToken);

        _logger.LogInformation(
            "ReferralJob {ReferralJobId} created by user {UserId} ({FilterType}, {Count} distinct plates); queuing matching job",
            referralJob.Id, userId, filterType, distinctPlates.Count);

        _backgroundJobClient.Enqueue<ReferralMatchingJob>(job =>
            job.ProcessAsync(referralJob.Id, CancellationToken.None));

        return ApiResponse<Guid>.SuccessResponse(referralJob.Id, "بدأنا نعالج الفرز في الخلفية.", 202);
    }

    private async Task BulkInsertReferralPlatesAsync(
        Guid referralJobId, Dictionary<string, string> distinctPlates, CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("referral_job_id", typeof(Guid));
        table.Columns.Add("raw_plate", typeof(string));
        table.Columns.Add("normalized_plate", typeof(string));
        table.Columns.Add("status", typeof(string));

        foreach (var (normalized, raw) in distinctPlates)
        {
            var dr = table.NewRow();
            dr["referral_job_id"] = referralJobId;
            dr["raw_plate"] = raw;
            dr["normalized_plate"] = normalized;
            dr["status"] = nameof(ReferralPlateStatus.Unmatched);
            table.Rows.Add(dr);
        }

        var connectionString = _dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Could not resolve the database connection string.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = "referral_plates",
            BulkCopyTimeout = 120
        };
        bulkCopy.ColumnMappings.Add("referral_job_id", "referral_job_id");
        bulkCopy.ColumnMappings.Add("raw_plate", "raw_plate");
        bulkCopy.ColumnMappings.Add("normalized_plate", "normalized_plate");
        bulkCopy.ColumnMappings.Add("status", "status");

        await bulkCopy.WriteToServerAsync(table, cancellationToken);
    }

    public async Task<ApiResponse<ReferralJobStatusDto>> GetStatusAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.ReferralJobs
            .AsNoTracking()
            .Where(j => j.Id == referralJobId)
            .Select(j => new ReferralJobStatusDto(
                j.Id, j.Status, j.FilterType, j.SourceType,
                j.TotalReferralPlates, j.SkippedPlatesCount, j.MatchedPlatesCount,
                j.TotalReferralPlates - j.SkippedPlatesCount - j.MatchedPlatesCount,
                j.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return job is null
            ? ApiResponse<ReferralJobStatusDto>.Fail("عملية الفرز غير موجودة.", null, 404)
            : ApiResponse<ReferralJobStatusDto>.SuccessResponse(job);
    }

    public async Task<ApiResponse<PagedResultDto<MatchResultRowDto>>> GetMatchedResultsAsync(
        Guid referralJobId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.MatchResults.AsNoTracking().Where(m => m.ReferralJobId == referralJobId);
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(m => m.NormalizedPlate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MatchResultRowDto(
                m.NormalizedPlate, m.FoundVehicleType, m.FoundDistrict, m.FoundStreet,
                m.Location, m.FoundRecordedAt, m.SourceDataFile.FileName))
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResultDto<MatchResultRowDto>>.SuccessResponse(
            new PagedResultDto<MatchResultRowDto>(items, totalCount, page, pageSize));
    }

    public async Task<ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>> GetUnmatchedResultsAsync(
        Guid referralJobId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ReferralPlates
            .AsNoTracking()
            .Where(p => p.ReferralJobId == referralJobId && p.Status == ReferralPlateStatus.Unmatched);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(p => p.NormalizedPlate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new UnmatchedPlateRowDto(p.RawPlate, p.NormalizedPlate))
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>.SuccessResponse(
            new PagedResultDto<UnmatchedPlateRowDto>(items, totalCount, page, pageSize));
    }

    public async Task<ApiResponse<AppendUnmatchedResultDto>> AppendUnmatchedToCheckFileAsync(
        Guid referralJobId, Guid currentUserId, AppendUnmatchedToCheckFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var userScope = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) return ApiResponse<AppendUnmatchedResultDto>.Fail("المستخدم غير موجود.");

        var activeCheckFile = await _dbContext.CheckFiles
            .Where(f => f.IsActive)
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == currentUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeCheckFile is null)
        {
            return ApiResponse<AppendUnmatchedResultDto>.Fail("مفيش ملف تشيك نشط حاليًا تقدر تلحق فيه.", null, 400);
        }

        var unmatchedPlates = await _dbContext.ReferralPlates
            .AsNoTracking()
            .Where(p => p.ReferralJobId == referralJobId && p.Status == ReferralPlateStatus.Unmatched)
            .Select(p => new { p.RawPlate, p.NormalizedPlate })
            .ToListAsync(cancellationToken);

        if (unmatchedPlates.Count == 0)
        {
            return ApiResponse<AppendUnmatchedResultDto>.SuccessResponse(new AppendUnmatchedResultDto(0, 0), "مفيش لوحات غير مفرزة أصلاً.");
        }

        var existingNormalizedPlates = await _dbContext.CheckPlates
            .AsNoTracking()
            .Where(cp => cp.CheckFileId == activeCheckFile.Id)
            .Select(cp => cp.NormalizedPlate)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingNormalizedPlates, StringComparer.OrdinalIgnoreCase);
        var toAppend = unmatchedPlates.Where(p => existingSet.Add(p.NormalizedPlate)).ToList();
        var skippedCount = unmatchedPlates.Count - toAppend.Count;

        if (toAppend.Count > 0)
        {
            var bankName = string.IsNullOrWhiteSpace(request.BankName) ? "غير محدد" : request.BankName.Trim();

            var newCheckPlates = toAppend.Select(p => new CheckPlate
            {
                CheckFileId = activeCheckFile.Id,
                RawPlate = p.RawPlate,
                NormalizedPlate = p.NormalizedPlate,
                BankName = bankName
            });

            await _dbContext.CheckPlates.AddRangeAsync(newCheckPlates, cancellationToken);

            await _dbContext.CheckFiles
                .Where(f => f.Id == activeCheckFile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.TotalPlates, f => f.TotalPlates + toAppend.Count), cancellationToken);

            await _unit.CompleteAsync();

            _checkPlateCache.InvalidateActiveCheckFileCache(userScope.GroupId, currentUserId);
        }

        _logger.LogInformation(
            "Appended {Count} unmatched plates from ReferralJob {ReferralJobId} into CheckFile {CheckFileId} ({Skipped} already existed)",
            toAppend.Count, referralJobId, activeCheckFile.Id, skippedCount);

        return ApiResponse<AppendUnmatchedResultDto>.SuccessResponse(new AppendUnmatchedResultDto(toAppend.Count, skippedCount));
    }

    public async Task<ApiResponse<WhatsAppShareLinkDto>> GetWhatsAppShareLinkAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.ReferralJobs
            .AsNoTracking()
            .Where(j => j.Id == referralJobId)
            .Select(j => new
            {
                j.TotalReferralPlates,
                j.SkippedPlatesCount,
                j.MatchedPlatesCount,
                j.Status
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null) return ApiResponse<WhatsAppShareLinkDto>.Fail("عملية الفرز غير موجودة.", null, 404);

        var unmatched = job.TotalReferralPlates - job.SkippedPlatesCount - job.MatchedPlatesCount;

        var message =
            $"نتائج الفرز:\n" +
            $"✅ لوحات مفرزة: {job.MatchedPlatesCount}\n" +
            $"⏭️ فرز من الإحالة: {job.SkippedPlatesCount}\n" +
            $"❌ غير مفرزة: {unmatched}";

        var shareUrl = WhatsAppShareHelper.BuildGenericShareLink(message);
        return ApiResponse<WhatsAppShareLinkDto>.SuccessResponse(new WhatsAppShareLinkDto(shareUrl, message));
    }
    //////////////////

    public async Task<ApiResponse<PagedResultDto<ReferralJobSummaryDto>>> GetReferralJobsAsync(
    Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var userScope = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) return ApiResponse<PagedResultDto<ReferralJobSummaryDto>>.Fail("المستخدم غير موجود.");

        var query = _dbContext.ReferralJobs.AsNoTracking();
        query = userScope.GroupId.HasValue
            ? query.Where(j => j.GroupId == userScope.GroupId.Value)
            : query.Where(j => j.GroupId == null && j.UserId == currentUserId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new ReferralJobSummaryDto(
                j.Id, j.FilterType, j.SourceType, j.Status,
                j.TotalReferralPlates, j.SkippedPlatesCount, j.MatchedPlatesCount,
                j.TotalReferralPlates - j.SkippedPlatesCount - j.MatchedPlatesCount,
                j.CreatedAt))
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResultDto<ReferralJobSummaryDto>>.SuccessResponse(
            new PagedResultDto<ReferralJobSummaryDto>(items, totalCount, page, pageSize));
    }

    public async Task<ApiResponse<bool>> DeleteAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var job = await _dbContext.ReferralJobs.FirstOrDefaultAsync(j => j.Id == referralJobId &&
            (userScope.GroupId.HasValue ? j.GroupId == userScope.GroupId.Value : j.GroupId == null && j.UserId == currentUserId),
            cancellationToken);

        if (job is null) return ApiResponse<bool>.Fail("عملية الفرز غير موجودة.", null, 404);

     
       await _unit.Repository<ReferralJob>().RemoveAsync(job);
        await _unit.CompleteAsync();

        _logger.LogInformation("ReferralJob {ReferralJobId} deleted by user {UserId}", referralJobId, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportMatchedAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        await EnsureJobInScopeAsync(referralJobId, currentUserId, cancellationToken);

        var rows = await _dbContext.MatchResults
            .AsNoTracking()
            .Where(m => m.ReferralJobId == referralJobId)
            .OrderBy(m => m.NormalizedPlate)
            .Select(m => new
            {
                m.NormalizedPlate,
                m.FoundVehicleType,
                m.FoundDistrict,
                m.FoundStreet,
                m.Location,
                m.FoundRecordedAt,
                SourceFileName = m.SourceDataFile.FileName
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("مطابقة");

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        sheet.Cell(1, 2).Value = "نوع السيارة";
        sheet.Cell(1, 3).Value = "الحي";
        sheet.Cell(1, 4).Value = "الشارع";
        sheet.Cell(1, 5).Value = "الموقع (GPS)";
        sheet.Cell(1, 6).Value = "تاريخ التسجيل";
        sheet.Cell(1, 7).Value = "مصدر الملف";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.NormalizedPlate;
            sheet.Cell(r, 2).Value = row.FoundVehicleType;
            sheet.Cell(r, 3).Value = row.FoundDistrict;
            sheet.Cell(r, 4).Value = row.FoundStreet;
            sheet.Cell(r, 5).Value = row.Location;
            sheet.Cell(r, 6).Value = row.FoundRecordedAt;
            sheet.Cell(r, 7).Value = row.SourceFileName;
            r++;
        }
        sheet.Columns().AdjustToContents();
        sheet.RightToLeft = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"نتائج-مفرزة-{referralJobId}.xlsx");
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportUnmatchedAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        await EnsureJobInScopeAsync(referralJobId, currentUserId, cancellationToken);

        var rows = await _dbContext.ReferralPlates
            .AsNoTracking()
            .Where(p => p.ReferralJobId == referralJobId && p.Status == ReferralPlateStatus.Unmatched)
            .OrderBy(p => p.NormalizedPlate)
            .Select(p => new { p.RawPlate, p.NormalizedPlate })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("غير مفرزة");

        sheet.Cell(1, 1).Value = "اللوحة كما أُدخلت";
        sheet.Cell(1, 2).Value = "اللوحة بعد التطبيع";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.RawPlate;
            sheet.Cell(r, 2).Value = row.NormalizedPlate;
            r++;
        }
        sheet.Columns().AdjustToContents();
        sheet.RightToLeft = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"غير-مفرزة-{referralJobId}.xlsx");
    }
    public async Task<(byte[] FileBytes, string FileName)> ExportResultsAsync(
    Guid referralJobId,
    Guid currentUserId,
    CancellationToken cancellationToken = default)
    {
        await EnsureJobInScopeAsync(
            referralJobId,
            currentUserId,
            cancellationToken);

     

        var matchedRows = await _dbContext.MatchResults
            .AsNoTracking()
            .Where(m => m.ReferralJobId == referralJobId)
            .OrderBy(m => m.NormalizedPlate)
            .Select(m => new
            {
                m.NormalizedPlate,
                m.FoundVehicleType,
                m.FoundDistrict,
                m.FoundStreet,
                m.Location,
                m.FoundRecordedAt,
                SourceFileName = m.SourceDataFile.FileName
            })
            .ToListAsync(cancellationToken);

      

        var unmatchedRows = await _dbContext.ReferralPlates
            .AsNoTracking()
            .Where(p =>
                p.ReferralJobId == referralJobId &&
                p.Status == ReferralPlateStatus.Unmatched)
            .OrderBy(p => p.NormalizedPlate)
            .Select(p => new
            {
                p.RawPlate,
                p.NormalizedPlate
            })
            .ToListAsync(cancellationToken);

     

        using var workbook = new XLWorkbook();

      

        var matchedSheet = workbook.Worksheets.Add("مطابقة");

        matchedSheet.Cell(1, 1).Value = "رقم اللوحة";
        matchedSheet.Cell(1, 2).Value = "نوع السيارة";
        matchedSheet.Cell(1, 3).Value = "الحي";
        matchedSheet.Cell(1, 4).Value = "الشارع";
        matchedSheet.Cell(1, 5).Value = "الموقع (GPS)";
        matchedSheet.Cell(1, 6).Value = "تاريخ التسجيل";
        matchedSheet.Cell(1, 7).Value = "مصدر الملف";

        var matchedRow = 2;

        foreach (var row in matchedRows)
        {
            matchedSheet.Cell(matchedRow, 1).Value = row.NormalizedPlate;
            matchedSheet.Cell(matchedRow, 2).Value = row.FoundVehicleType;
            matchedSheet.Cell(matchedRow, 3).Value = row.FoundDistrict;
            matchedSheet.Cell(matchedRow, 4).Value = row.FoundStreet;
            matchedSheet.Cell(matchedRow, 5).Value = row.Location;
            matchedSheet.Cell(matchedRow, 6).Value = row.FoundRecordedAt;
            matchedSheet.Cell(matchedRow, 7).Value = row.SourceFileName;

            matchedRow++;
        }

        matchedSheet.Columns().AdjustToContents();
        matchedSheet.RightToLeft = true;

       

        var unmatchedSheet = workbook.Worksheets.Add("غير مفرزة");

        unmatchedSheet.Cell(1, 1).Value = "اللوحة كما أُدخلت";
        unmatchedSheet.Cell(1, 2).Value = "اللوحة بعد التطبيع";

        var unmatchedRow = 2;

        foreach (var row in unmatchedRows)
        {
            unmatchedSheet.Cell(unmatchedRow, 1).Value = row.RawPlate;
            unmatchedSheet.Cell(unmatchedRow, 2).Value = row.NormalizedPlate;

            unmatchedRow++;
        }

        unmatchedSheet.Columns().AdjustToContents();
        unmatchedSheet.RightToLeft = true;

      

        using var stream = new MemoryStream();

        workbook.SaveAs(stream);

        return (
            stream.ToArray(),
            $"نتائج-الإحالة-{referralJobId}.xlsx"
        );
    }
    private async Task EnsureJobInScopeAsync(Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var userScope = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var exists = await _dbContext.ReferralJobs.AsNoTracking().AnyAsync(j => j.Id == referralJobId &&
            (userScope.GroupId.HasValue ? j.GroupId == userScope.GroupId.Value : j.GroupId == null && j.UserId == currentUserId),
            cancellationToken);

        if (!exists) throw new KeyNotFoundException("عملية الفرز غير موجودة.");
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

}
