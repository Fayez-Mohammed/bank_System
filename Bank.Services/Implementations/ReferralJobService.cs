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
using System.Text.Json;
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

    // لوحة إحالة واحدة بعد قراءتها من الملف — القيمة الخام + باقي أعمدة نفس الصف كـ JSON.
    private sealed record RawReferralPlate(string RawPlate, string? ExtraDataJson);

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
    public async Task<(byte[] FileBytes, string FileName)> ExportCombinedResultsAsync(
    List<Guid> referralJobIds, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        if (referralJobIds is null || referralJobIds.Count == 0)
        {
            throw new ArgumentException("لازم تختار عملية فرز واحدة على الأقل.");
        }

        var distinctIds = referralJobIds.Distinct().ToList();
        var jobs = new List<ReferralJob>();

        foreach (var id in distinctIds)
        {
            var job = await GetJobInScopeAsync(id, currentUserId, cancellationToken)
                ?? throw new KeyNotFoundException($"عملية فرز غير موجودة أو مش تبعتك ({id}).");
            jobs.Add(job);
        }

        var data = await BuildCombinedResultsAsync(jobs, cancellationToken);

        using var workbook = new XLWorkbook();
        WriteMatchedSheet(workbook, "التطابقات", data);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"نتيجة-مجمّعة-{jobs.Count}-عمليات-{DateTime.UtcNow:yyyy-MM-dd}.xlsx";
        return (stream.ToArray(), fileName);
    }

    private async Task<ReferralResultsResponseDto> BuildCombinedResultsAsync(
     List<ReferralJob> jobs, CancellationToken cancellationToken)
    {
        var jobIds = jobs.Select(j => j.Id).ToList();

        // أولوية كل عملية فرز حسب ترتيبها في الاختيار (اللي جاي الأول بياخد الأولوية
        // لو نفس الصف ظهر في أكتر من عملية).
        var jobPriority = jobs.Select((j, idx) => (j.Id, idx)).ToDictionary(x => x.Id, x => x.idx);

        var selectedHistorical = jobs
            .SelectMany(j => DeserializeHistoricalColumns(j.SelectedHistoricalColumnsJson))
            .Distinct()
            .OrderBy(c => (int)c)
            .ToList();

        var selectedReferral = jobs
            .SelectMany(j => DeserializeStringList(j.ReferralColumnsJson))
            .Distinct()
            .ToList();

        var allRows = await _dbContext.MatchResults
            .AsNoTracking()
            .Where(m => jobIds.Contains(m.ReferralJobId))
            .Select(m => new
            {
                m.Id,
                m.ReferralJobId,
                m.HistoricalPlateId,
                m.NormalizedPlate,
                m.FoundVehicleType,
                m.FoundDistrict,
                m.FoundStreet,
                m.Location,
                m.FoundRecordedAt,
                m.SourceDataFileId,
                SourceFileName = m.SourceDataFile.FileName,
                SortOrder = m.SourceDataFile.SortOrder,
                RawPlate = m.HistoricalPlate.RawPlate,
                Notes = m.HistoricalPlate.Notes,
                RecorderName = m.HistoricalPlate.RecorderName
            })
            .ToListAsync(cancellationToken);

        // دي الإضافة الجديدة: نفس صف الداتا (HistoricalPlateId) ممكن يكون matched في
        // أكتر من عملية فرز من اللي اختارهم المستخدم — نسيب نسخة واحدة بس منه في
        // النتيجة المجمّعة، حسب أولوية ترتيب العمليات المختارة.
        var rows = allRows
            .GroupBy(r => r.HistoricalPlateId)
            .Select(g => g.OrderBy(r => jobPriority[r.ReferralJobId]).First())
            .ToList();

        var extraByJobAndPlate = new Dictionary<string, string?>();
        if (selectedReferral.Count > 0)
        {
            var referralExtras = await _dbContext.ReferralPlates
                .AsNoTracking()
                .Where(p => jobIds.Contains(p.ReferralJobId))
                .Select(p => new { p.ReferralJobId, p.NormalizedPlate, p.ExtraDataJson })
                .ToListAsync(cancellationToken);

            foreach (var e in referralExtras)
            {
                extraByJobAndPlate[$"{e.ReferralJobId}|{e.NormalizedPlate}"] = e.ExtraDataJson;
            }
        }

        // التلوين بعد الـ Dedupe — لو نفس رقم اللوحة لسه ظاهر في أكتر من صف حقيقي
        // مختلف (HistoricalPlateId مختلف)، ده تكرار فعلي وبيفضل ملوّن زي الأول.
        var colorByPlate = rows
            .GroupBy(r => r.NormalizedPlate)
            .Where(g => g.Count() > 1)
            .Select((g, idx) => new { g.Key, Color = idx % ReferralColorPalette.Colors.Count })
            .ToDictionary(x => x.Key, x => x.Color);

        var groups = rows
            .GroupBy(r => new { r.SourceDataFileId, r.SourceFileName, r.SortOrder })
            .OrderBy(g => g.Key.SortOrder)
            .Select(g => new ReferralResultGroupDto(
                g.Key.SourceDataFileId,
                g.Key.SourceFileName,
                g.Key.SortOrder,
                g.OrderBy(r => r.NormalizedPlate).Select(r =>
                {
                    var dataCols = BuildDataColumns(
                        selectedHistorical, r.FoundVehicleType, r.FoundDistrict, r.FoundStreet,
                        r.Location, r.FoundRecordedAt, r.Notes, r.RecorderName);

                    extraByJobAndPlate.TryGetValue($"{r.ReferralJobId}|{r.NormalizedPlate}", out var extraJson);
                    var referralCols = BuildReferralColumns(selectedReferral, extraJson);

                    var hasColor = colorByPlate.TryGetValue(r.NormalizedPlate, out var colorIdx);

                    return new ReferralResultRowDto(
                        r.Id, r.HistoricalPlateId, r.NormalizedPlate, r.RawPlate,
                        hasColor ? colorIdx : null,
                        dataCols, referralCols);
                }).ToList()))
            .ToList();

        return new ReferralResultsResponseDto(
            selectedHistorical.Select(c => c.ToString()).ToList(),
            selectedReferral,
            groups);
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

        var sheetDtos = sheets.Select(s =>
        {
            var plateColumn = _columnDetector.DetectPlateColumn(s.Headers, s.SampleRows);
            var (colorHeader, vehicleTypeHeader, modelHeader) = ReferralColumnSuggestor.SuggestDefaults(s.Headers);
            var suggestions = new SuggestedReferralColumnsDto(plateColumn, colorHeader, vehicleTypeHeader, modelHeader);
            return new SheetInfoDto(s.Index, s.Name, s.RowCount, s.Headers, suggestions, s.HeaderRowIndex);
        }).ToList();

        return ApiResponse<InspectReferralFileResponseDto>.SuccessResponse(new InspectReferralFileResponseDto(sheetDtos));
    }
    public async Task<ApiResponse<Guid>> CreateFromUploadAsync(
     Guid userId, CreateReferralJobUploadRequestDto request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateUploadedFile(request.File);
        if (validationError is not null) return ApiResponse<Guid>.Fail(validationError, null, 400);

        if (request.Sheets is null || request.Sheets.Count == 0)
        {
            return ApiResponse<Guid>.Fail("لازم تختار Sheet واحدة على الأقل وتحدد عمود رقم اللوحة فيها.", null, 400);
        }

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        var selectedIndexes = request.Sheets.Select(s => s.SheetIndex).ToList();

        List<ExcelSheetData> sheets;
        await using (var stream = request.File.OpenReadStream())
        {
            sheets = ExcelSheetReader.ReadSelectedSheets(stream, extension, allSheets: false, selectedIndexes);
        }

        var plateColumnBySheet = request.Sheets.ToDictionary(s => s.SheetIndex, s => s.PlateColumnIndex);
        var headerRowIndexBySheet = request.Sheets.ToDictionary(s => s.SheetIndex, s => s.HeaderRowIndex);
        var rawPlates = new List<RawReferralPlate>();

        foreach (var sheet in sheets)
        {
            if (!plateColumnBySheet.TryGetValue(sheet.Index, out var plateColIdx)) continue;

            var headerRowIdx = headerRowIndexBySheet.TryGetValue(sheet.Index, out var hIdx) ? hIdx : 0;
            if (headerRowIdx < 0 || headerRowIdx >= sheet.Rows.Count)
            {
                _logger.LogWarning("Invalid header row index {Index} for sheet '{SheetName}' — skipping sheet", headerRowIdx, sheet.Name);
                continue;
            }

            var headerRow = sheet.Rows[headerRowIdx];
            if (plateColIdx < 0 || plateColIdx >= headerRow.Count)
            {
                _logger.LogWarning("Invalid plate column index {Index} for sheet '{SheetName}' — skipping sheet", plateColIdx, sheet.Name);
                continue;
            }

            var dataRows = sheet.Rows.Skip(headerRowIdx + 1);

            foreach (var dataRow in dataRows)
            {
                if (plateColIdx >= dataRow.Count) continue;
                var rawPlate = dataRow[plateColIdx]?.Trim();
                if (string.IsNullOrWhiteSpace(rawPlate)) continue;

                // نخزّن كل أعمدة الصف الأخرى (مش بس المختار للعرض) عشان لو المستخدم
                // غيّر اختيار الأعمدة بعدين ما يبقاش محتاج يرفع الملف تاني.
                Dictionary<string, string?>? extra = null;
                for (var i = 0; i < headerRow.Count && i < dataRow.Count; i++)
                {
                    if (i == plateColIdx) continue;
                    var header = headerRow[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(header)) continue;
                    var value = dataRow[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    extra ??= new Dictionary<string, string?>();
                    extra[header] = value;
                }

                rawPlates.Add(new RawReferralPlate(rawPlate, extra is null ? null : JsonSerializer.Serialize(extra)));
            }
        }

        if (rawPlates.Count == 0)
        {
            return ApiResponse<Guid>.Fail("مقدرناش نلاقي أي لوحات صالحة في الـ Sheets اللي اخترتها.", null, 422);
        }

        return await CreateReferralJobInternalAsync(
            userId, request.FilterType, SourceType.FileUpload, request.File.FileName,
            request.Password, rawPlates, request.SelectedReferralColumns, request.SelectedHistoricalColumns,
            cancellationToken);
    }
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
            .Select(v => new RawReferralPlate(v, null))
            .ToList();

        return await CreateReferralJobInternalAsync(
            userId, request.FilterType, SourceType.DirectPaste, null, null, rawPlates,
            selectedReferralColumns: null, request.SelectedHistoricalColumns, cancellationToken);
    }

    private async Task<ApiResponse<Guid>> CreateReferralJobInternalAsync(
        Guid userId, FilterType filterType, SourceType sourceType, string? sourceFileName,
        string? password, List<RawReferralPlate> rawPlates,
        List<string>? selectedReferralColumns, List<HistoricalDataColumn>? selectedHistoricalColumns,
        CancellationToken cancellationToken)
    {
        var userScope = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) return ApiResponse<Guid>.Fail("المستخدم غير موجود.");

        var distinctPlates = new Dictionary<string, RawReferralPlate>();
        foreach (var raw in rawPlates)
        {
            var normalized = PlateNormalizer.NormalizeReferralPlate(raw.RawPlate);
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
            Status = JobStatus.Processing,
            SelectedHistoricalColumnsJson = JsonSerializer.Serialize(
                selectedHistoricalColumns is { Count: > 0 } ? selectedHistoricalColumns : Enum.GetValues<HistoricalDataColumn>().ToList()),
            ReferralColumnsJson = JsonSerializer.Serialize(selectedReferralColumns ?? new List<string>())
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
        Guid referralJobId, Dictionary<string, RawReferralPlate> distinctPlates, CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("referral_job_id", typeof(Guid));
        table.Columns.Add("raw_plate", typeof(string));
        table.Columns.Add("normalized_plate", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("extra_data_json", typeof(string));

        foreach (var (normalized, raw) in distinctPlates)
        {
            var dr = table.NewRow();
            dr["referral_job_id"] = referralJobId;
            dr["raw_plate"] = raw.RawPlate;
            dr["normalized_plate"] = normalized;
            dr["status"] = nameof(ReferralPlateStatus.Unmatched);
            dr["extra_data_json"] = (object?)raw.ExtraDataJson ?? DBNull.Value;
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
        bulkCopy.ColumnMappings.Add("extra_data_json", "extra_data_json");

        await bulkCopy.WriteToServerAsync(table, cancellationToken);
    }

    public async Task<ApiResponse<ReferralJobStatusDto>> GetStatusAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken);
        if (job is null) return ApiResponse<ReferralJobStatusDto>.Fail("عملية الفرز غير موجودة.", null, 404);

        var unmatched = job.TotalReferralPlates - job.SkippedPlatesCount - job.MatchedPlatesCount;
        var dto = new ReferralJobStatusDto(
            job.Id, job.Status, job.FilterType, job.SourceType,
            job.TotalReferralPlates, job.SkippedPlatesCount, job.MatchedPlatesCount, unmatched, job.CreatedAt);

        return ApiResponse<ReferralJobStatusDto>.SuccessResponse(dto);
    }

    public async Task<ApiResponse<ReferralResultsResponseDto>> GetResultsAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken);
        if (job is null) return ApiResponse<ReferralResultsResponseDto>.Fail("عملية الفرز غير موجودة.", null, 404);

        var data = await BuildResultsAsync(job, cancellationToken);
        return ApiResponse<ReferralResultsResponseDto>.SuccessResponse(data);
    }

    public async Task<ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>> GetUnmatchedResultsAsync(
        Guid referralJobId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken);
        if (job is null) return ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>.Fail("عملية الفرز غير موجودة.", null, 404);

        var query = _dbContext.ReferralPlates
            .AsNoTracking()
            .Where(p => p.ReferralJobId == referralJobId && p.Status == ReferralPlateStatus.Unmatched);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(p => p.NormalizedPlate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new UnmatchedPlateRowDto(p.Id, p.RawPlate, p.NormalizedPlate))
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>.SuccessResponse(
            new PagedResultDto<UnmatchedPlateRowDto>(items, totalCount, page, pageSize));
    }

    public async Task<ApiResponse<AppendUnmatchedResultDto>> AppendAllToCheckFileAsync(
        Guid referralJobId, Guid currentUserId, AppendUnmatchedToCheckFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken);
        if (job is null) return ApiResponse<AppendUnmatchedResultDto>.Fail("عملية الفرز غير موجودة.", null, 404);

        var groupId = job.GroupId;

        var activeCheckFile = await _dbContext.CheckFiles
            .Where(f => f.IsActive)
            .Where(f => groupId.HasValue
                ? f.GroupId == groupId.Value
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

        // اللوحات المفرزة (matched) بترجع من match_results، والقيمة الخام بتتاخد من
        // HistoricalPlate.RawPlate بتاعها — دي اللوحة زي ما اتلاقت فعليًا في الداتا التاريخية.
        var matchedPlates = await _dbContext.MatchResults
            .AsNoTracking()
            .Where(m => m.ReferralJobId == referralJobId)
            .Select(m => new { RawPlate = m.HistoricalPlate.RawPlate, m.NormalizedPlate })
            .ToListAsync(cancellationToken);

        // دمج مفرزة + غير مفرزة، وإزالة التكرار بالـ NormalizedPlate (أول ظهور بيكسب) —
        // لأن اللوحة الواحدة ممكن يكون ليها أكتر من صف matched لو اتلاقت في أكتر من ملف داتا.
        var combinedByPlate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in matchedPlates.Concat(unmatchedPlates))
        {
            combinedByPlate.TryAdd(p.NormalizedPlate, p.RawPlate);
        }

        if (combinedByPlate.Count == 0)
        {
            return ApiResponse<AppendUnmatchedResultDto>.SuccessResponse(new AppendUnmatchedResultDto(0, 0), "مفيش لوحات (مفرزة ولا غير مفرزة) في عملية الفرز دي أصلاً.");
        }

        var existingNormalizedPlates = await _dbContext.CheckPlates
            .AsNoTracking()
            .Where(cp => cp.CheckFileId == activeCheckFile.Id)
            .Select(cp => cp.NormalizedPlate)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingNormalizedPlates, StringComparer.OrdinalIgnoreCase);
        var toAppend = combinedByPlate.Where(p => existingSet.Add(p.Key)).ToList();
        var skippedCount = combinedByPlate.Count - toAppend.Count;

        if (toAppend.Count > 0)
        {
            var bankName = string.IsNullOrWhiteSpace(request.BankName) ? "غير محدد" : request.BankName.Trim();

            var newCheckPlates = toAppend.Select(p => new CheckPlate
            {
                CheckFileId = activeCheckFile.Id,
                RawPlate = p.Value,
                NormalizedPlate = p.Key,
                BankName = bankName
            });

            await _dbContext.CheckPlates.AddRangeAsync(newCheckPlates, cancellationToken);

            await _dbContext.CheckFiles
                .Where(f => f.Id == activeCheckFile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.TotalPlates, f => f.TotalPlates + toAppend.Count), cancellationToken);

            await _unit.CompleteAsync();

            _checkPlateCache.InvalidateActiveCheckFileCache(groupId, currentUserId);
        }

        _logger.LogInformation(
            "Appended {Count} plates (matched + unmatched) from ReferralJob {ReferralJobId} into CheckFile {CheckFileId} ({Skipped} already existed)",
            toAppend.Count, referralJobId, activeCheckFile.Id, skippedCount);

        return ApiResponse<AppendUnmatchedResultDto>.SuccessResponse(new AppendUnmatchedResultDto(toAppend.Count, skippedCount));
    }
    public async Task<ApiResponse<WhatsAppShareLinkDto>> GetWhatsAppShareLinkAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken);
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

    public async Task<ApiResponse<DeleteMatchedPlateResponseDto>> DeleteMatchedPlateAsync(
        Guid referralJobId, long matchResultId, Guid currentUserId, bool deleteFromDataFile, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken);
        if (job is null) return ApiResponse<DeleteMatchedPlateResponseDto>.Fail("عملية الفرز غير موجودة.", null, 404);

        var match = await _dbContext.MatchResults
            .FirstOrDefaultAsync(m => m.Id == matchResultId && m.ReferralJobId == referralJobId, cancellationToken);

        if (match is null)
        {
            return ApiResponse<DeleteMatchedPlateResponseDto>.Fail("اللوحة غير موجودة في نتيجة الفرز.", null, 404);
        }

        // مسح من نتيجة الفرز بس — صف match_result واحد، والداتا التاريخية متتأثرش.
        if (!deleteFromDataFile)
        {
            _dbContext.MatchResults.Remove(match);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "MatchResult {MatchResultId} removed from ReferralJob {ReferralJobId} results only (data kept) by user {UserId}",
                matchResultId, referralJobId, currentUserId);

            return ApiResponse<DeleteMatchedPlateResponseDto>.SuccessResponse(new DeleteMatchedPlateResponseDto(false));
        }

        // مسح من الداتا نهائي — الصف بيتشال من historical_plates + أي match_results
        // مرتبطة بيه (في كل عمليات الفرز، مش بس دي)، وrow_count بينقّص.
        var historicalPlateId = match.HistoricalPlateId;
        var dataFileId = match.SourceDataFileId;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.MatchResults
                .Where(m => m.HistoricalPlateId == historicalPlateId)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedRows = await _dbContext.HistoricalPlates
                .Where(h => h.Id == historicalPlateId)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows > 0)
            {
                await _dbContext.DataFiles
                    .Where(f => f.Id == dataFileId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.RowCount, f => f.RowCount - 1), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "HistoricalPlate {HistoricalPlateId} permanently deleted from DataFile {DataFileId} (triggered from ReferralJob {ReferralJobId}) by user {UserId}",
            historicalPlateId, dataFileId, referralJobId, currentUserId);

        return ApiResponse<DeleteMatchedPlateResponseDto>.SuccessResponse(new DeleteMatchedPlateResponseDto(true));
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportMatchedAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken)
            ?? throw new KeyNotFoundException("عملية الفرز غير موجودة.");

        var data = await BuildResultsAsync(job, cancellationToken);

        using var workbook = new XLWorkbook();
        WriteMatchedSheet(workbook, "مطابقة", data);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"نتائج-مفرزة-{referralJobId}.xlsx");
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportUnmatchedAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        _ = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken)
            ?? throw new KeyNotFoundException("عملية الفرز غير موجودة.");

        var rows = await _dbContext.ReferralPlates
            .AsNoTracking()
            .Where(p => p.ReferralJobId == referralJobId && p.Status == ReferralPlateStatus.Unmatched)
            .OrderBy(p => p.NormalizedPlate)
            .Select(p => new { p.RawPlate, p.NormalizedPlate })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        WriteUnmatchedSheet(workbook, "غير مفرزة", rows.Select(r => (r.RawPlate, r.NormalizedPlate)).ToList());

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"غير-مفرزة-{referralJobId}.xlsx");
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportResultsAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var job = await GetJobInScopeAsync(referralJobId, currentUserId, cancellationToken)
            ?? throw new KeyNotFoundException("عملية الفرز غير موجودة.");

        var matchedData = await BuildResultsAsync(job, cancellationToken);

        var unmatchedRows = await _dbContext.ReferralPlates
            .AsNoTracking()
            .Where(p => p.ReferralJobId == referralJobId && p.Status == ReferralPlateStatus.Unmatched)
            .OrderBy(p => p.NormalizedPlate)
            .Select(p => new { p.RawPlate, p.NormalizedPlate })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        WriteMatchedSheet(workbook, "التطابقات", matchedData);
        WriteUnmatchedSheet(workbook, "غير مفرزة", unmatchedRows.Select(r => (r.RawPlate, r.NormalizedPlate)).ToList());

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return (stream.ToArray(), $"نتائج-الإحالة-{referralJobId}.xlsx");
    }

    // ---------- Helpers ----------

    private async Task<ReferralJob?> GetJobInScopeAsync(Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var userScope = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.GroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (userScope is null) return null;

        return await _dbContext.ReferralJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == referralJobId &&
                (userScope.GroupId.HasValue ? j.GroupId == userScope.GroupId.Value : j.GroupId == null && j.UserId == currentUserId),
                cancellationToken);
    }

    private async Task<ReferralResultsResponseDto> BuildResultsAsync(ReferralJob job, CancellationToken cancellationToken)
    {
        var selectedHistorical = DeserializeHistoricalColumns(job.SelectedHistoricalColumnsJson);
        var selectedReferral = DeserializeStringList(job.ReferralColumnsJson);

          var rows = await _dbContext.MatchResults
            .AsNoTracking()
            .Where(m => m.ReferralJobId == job.Id)
            .Select(m => new
            {
                m.Id,
                m.HistoricalPlateId,
                m.NormalizedPlate,
                m.FoundVehicleType,
                m.FoundDistrict,
                m.FoundStreet,
                m.Location,
                m.FoundRecordedAt,
                m.SourceDataFileId,
                SourceFileName = m.SourceDataFile.FileName,
                SortOrder = m.SourceDataFile.SortOrder,
                RawPlate = m.HistoricalPlate.RawPlate,
                Notes = m.HistoricalPlate.Notes,
                RecorderName = m.HistoricalPlate.RecorderName
            })
            .ToListAsync(cancellationToken);

        var extraByPlate = new Dictionary<string, string?>();
        if (selectedReferral.Count > 0)
        {
            extraByPlate = await _dbContext.ReferralPlates
                .AsNoTracking()
                .Where(p => p.ReferralJobId == job.Id)
                .Select(p => new { p.NormalizedPlate, p.ExtraDataJson })
                .ToDictionaryAsync(p => p.NormalizedPlate, p => p.ExtraDataJson, cancellationToken);
        }

        // اللوحة المتكررة (في أكتر من صف) بتاخد نفس اللون على طول النتيجة،
        // بترتيب أول ظهور. اللوحة الفريدة تفضل من غير تلوين.
        var colorByPlate = rows
            .GroupBy(r => r.NormalizedPlate)
            .Where(g => g.Count() > 1)
            .Select((g, idx) => new { g.Key, Color = idx % ReferralColorPalette.Colors.Count })
            .ToDictionary(x => x.Key, x => x.Color);

        var groups = rows
            .GroupBy(r => new { r.SourceDataFileId, r.SourceFileName, r.SortOrder })
            .OrderBy(g => g.Key.SortOrder)
            .Select(g => new ReferralResultGroupDto(
                g.Key.SourceDataFileId,
                g.Key.SourceFileName,
                g.Key.SortOrder,
                g.OrderBy(r => r.NormalizedPlate).Select(r =>
                {
                    var dataCols = BuildDataColumns(
                        selectedHistorical, r.FoundVehicleType, r.FoundDistrict, r.FoundStreet,
                        r.Location, r.FoundRecordedAt, r.Notes, r.RecorderName);

                    extraByPlate.TryGetValue(r.NormalizedPlate, out var extraJson);
                    var referralCols = BuildReferralColumns(selectedReferral, extraJson);

                    var hasColor = colorByPlate.TryGetValue(r.NormalizedPlate, out var colorIdx);

                    return new ReferralResultRowDto(
                        r.Id, r.HistoricalPlateId, r.NormalizedPlate, r.RawPlate,
                        hasColor ? colorIdx : null,
                        dataCols, referralCols);
                }).ToList()))
            .ToList();

        return new ReferralResultsResponseDto(
            selectedHistorical.Select(c => c.ToString()).ToList(),
            selectedReferral,
            groups);
    }

    private static Dictionary<string, string?> BuildDataColumns(
        List<HistoricalDataColumn> selected, string? vehicleType, string? district, string? street,
        string? location, string? recordedAt, string? notes, string? recorderName)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var col in selected)
        {
            dict[col.ToString()] = col switch
            {
                HistoricalDataColumn.VehicleType => vehicleType,
                HistoricalDataColumn.District => district,
                HistoricalDataColumn.Street => street,
                HistoricalDataColumn.Location => location,
                HistoricalDataColumn.RecordedAt => recordedAt,
                HistoricalDataColumn.Notes => notes,
                HistoricalDataColumn.RecorderName => recorderName,
                _ => null
            };
        }
        return dict;
    }

    private static Dictionary<string, string?> BuildReferralColumns(List<string> selectedHeaders, string? extraJson)
    {
        var dict = new Dictionary<string, string?>();
        if (selectedHeaders.Count == 0) return dict;

        Dictionary<string, string?>? extra = null;
        if (!string.IsNullOrWhiteSpace(extraJson))
        {
            try { extra = JsonSerializer.Deserialize<Dictionary<string, string?>>(extraJson); }
            catch (JsonException) { extra = null; }
        }

        foreach (var header in selectedHeaders)
        {
            dict[header] = extra != null && extra.TryGetValue(header, out var v) ? v : null;
        }
        return dict;
    }

    private static List<HistoricalDataColumn> DeserializeHistoricalColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Enum.GetValues<HistoricalDataColumn>().ToList();
        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            var parsed = names
                .Select(n => Enum.TryParse<HistoricalDataColumn>(n, out var v) ? v : (HistoricalDataColumn?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            return parsed.Count > 0 ? parsed : Enum.GetValues<HistoricalDataColumn>().ToList();
        }
        catch (JsonException) { return Enum.GetValues<HistoricalDataColumn>().ToList(); }
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private static string HistoricalColumnLabel(string columnName) => columnName switch
    {
        nameof(HistoricalDataColumn.VehicleType) => "نوع السيارة",
        nameof(HistoricalDataColumn.District) => "الحي",
        nameof(HistoricalDataColumn.Street) => "الشارع",
        nameof(HistoricalDataColumn.Location) => "GPS",
        nameof(HistoricalDataColumn.RecordedAt) => "تاريخ التسجيل",
        nameof(HistoricalDataColumn.Notes) => "ملاحظات",
        nameof(HistoricalDataColumn.RecorderName) => "اسم المسجّل",
        _ => columnName
    };

    private static readonly string[] GroupBannerColors = { "2F5496", "C55A11", "548235", "7030A0", "BF8F00", "1F4E78" };
    private static readonly string[] GroupHeaderColors = { "1F3864", "833C00", "375623", "4C1F6D", "806000", "0F2A44" };
    private static void WriteMatchedSheet(XLWorkbook workbook, string sheetName, ReferralResultsResponseDto data)
    {
        var sheet = workbook.Worksheets.Add(sheetName);
        sheet.RightToLeft = true;

        var historicalHeaders = data.SelectedHistoricalColumns.Select(HistoricalColumnLabel).ToList();
        var referralHeaders = data.SelectedReferralColumns;
        var totalCols = 1 + historicalHeaders.Count + referralHeaders.Count;
        var locationColIndex = data.SelectedHistoricalColumns.IndexOf(nameof(HistoricalDataColumn.Location));

        var row = 1;
        var groupIndex = 0;

        foreach (var group in data.Groups)
        {
            var bannerColor = XLColor.FromHtml("#" + GroupBannerColors[groupIndex % GroupBannerColors.Length]);
            var headerColor = XLColor.FromHtml("#" + GroupHeaderColors[groupIndex % GroupHeaderColors.Length]);
            groupIndex++;

            var titleCell = sheet.Cell(row, 1);
            titleCell.Value = $"📁 {group.DataFileName}";
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 13;
            titleCell.Style.Font.FontColor = XLColor.White;
            titleCell.Style.Fill.BackgroundColor = bannerColor;
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            if (totalCols > 1) sheet.Range(row, 1, row, totalCols).Merge();
            sheet.Row(row).Height = 24;
            row++;

            var headerRow = row;
            sheet.Cell(headerRow, 1).Value = "رقم اللوحة";
            var headerCol = 2;
            foreach (var h in historicalHeaders) sheet.Cell(headerRow, headerCol++).Value = h;
            foreach (var h in referralHeaders) sheet.Cell(headerRow, headerCol++).Value = h;

            var headerRange = sheet.Range(headerRow, 1, headerRow, totalCols);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + GroupHeaderColors[groupIndex % GroupHeaderColors.Length]); headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            sheet.Row(headerRow).Height = 20;
            row++;

            var firstDataRow = row;

            foreach (var r in group.Rows)
            {
                var plateCell = sheet.Cell(row, 1);
                plateCell.Value = r.NormalizedPlate;
                plateCell.Style.Font.Bold = true;
                plateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                plateCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                if (r.ColorIndex.HasValue)
                {
                    plateCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + ReferralColorPalette.Colors[r.ColorIndex.Value]);
                }

                var col = 2;
                foreach (var key in data.SelectedHistoricalColumns)
                {
                    var cell = sheet.Cell(row, col);
                    var value = r.DataColumns.TryGetValue(key, out var v) ? v : null;
                    cell.Value = value;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                    if (key == nameof(HistoricalDataColumn.Location) && !string.IsNullOrWhiteSpace(value)
                        && Uri.TryCreate(value, UriKind.Absolute, out var uri))
                    {
                        cell.Value = "فتح الموقع";
                        cell.SetHyperlink(new XLHyperlink(uri.AbsoluteUri));
                        cell.Style.Font.FontColor = XLColor.FromHtml("#0563C1");
                        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }

                    if (r.ColorIndex.HasValue)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + ReferralColorPalette.Colors[r.ColorIndex.Value]);
                    }
                    col++;
                }

                foreach (var key in referralHeaders)
                {
                    var cell = sheet.Cell(row, col);
                    cell.Value = r.ReferralColumns.TryGetValue(key, out var v) ? v : null;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + ReferralColorPalette.ReferralColumnBackground);
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    col++;
                }
                row++;
            }

            if (row > firstDataRow)
            {
                var dataRange = sheet.Range(firstDataRow, 1, row - 1, totalCols);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            }

            row++; 
        }

        sheet.Columns().AdjustToContents();

        if (locationColIndex >= 0)
        {
            sheet.Column(2 + locationColIndex).Width = 14;
        }

        sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 14);
        sheet.SheetView.FreezeColumns(1); 
    }

    private static void WriteUnmatchedSheet(XLWorkbook workbook, string sheetName, List<(string RawPlate, string NormalizedPlate)> rows)
    {
        var sheet = workbook.Worksheets.Add(sheetName);
        sheet.RightToLeft = true;

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

