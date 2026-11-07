using System.Text.Json;
using System.Text.RegularExpressions;
using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Helpers;
using Bank.Services.HangFireJobs;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CertificateDTOs;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Bank.Services.Interfaces.ICertificate;

namespace Bank.Services.Implementations.CertificateServices;

public class CertificateService : ICertificateService
{
    private static readonly Regex LettersPattern = new(@"^[\u0621-\u064A]{1,3}$", RegexOptions.Compiled);
    private static readonly Regex DigitsPattern = new(@"^[0-9]{1,4}$", RegexOptions.Compiled);

    // أعمدة "نتيجة الفرز" الإضافية: بنفس شكل أعمدة ملف الإحالة (الاسم = رأس العمود).
    private const string ColFileName = "اسم الملف";
    private const string ColVehicleType = "النوع";
    private const string ColColor = "اللون";
    private const string ColLessor = "المؤجر";

    private readonly AppDbContext _db;
    private readonly IGoogleDriveClient _drive;
    private readonly IFileStorageService _storage;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<CertificateService> _logger;
    private readonly IPermissionService _permissions;
    private readonly ICertificateWorkGate _workGate;

    public CertificateService(
        AppDbContext db,
        IGoogleDriveClient drive,
        IFileStorageService storage,
        IBackgroundJobClient jobs,
        ILogger<CertificateService> logger,
        IPermissionService permissions,
        ICertificateWorkGate workGate)
    {
        _db = db;
        _drive = drive;
        _storage = storage;
        _jobs = jobs;
        _logger = logger;
        _permissions = permissions;
        _workGate = workGate;
    }

    /// <summary>آخر دفعة حقيقية. الملفات التاريخية (BatchNo = 0) مش دفعات.</summary>
    private async Task<int?> LatestBatchNoAsync(CancellationToken ct) =>
        await _db.Certificates.Where(c => c.BatchNo > 0).MaxAsync(c => (int?)c.BatchNo, ct);

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // =====================================================================
    //  List / Detail
    // =====================================================================
    public async Task<ApiResponse<PagedResult<CertificateListItemDto>>> ListAsync(
        CertificateQueryDto query, CancellationToken ct = default)
    {
        // الملفات اللي اتمسحت من Drive مش بتظهر (مش هينفع تتعرض أو تتنزّل).
        IQueryable<Certificate> q = _db.Certificates.AsNoTracking().Where(c => !c.IsDeletedInDrive);

        if (!string.IsNullOrWhiteSpace(query.Origin) &&
            Enum.TryParse<CertificateOrigin>(query.Origin, true, out var origin))
        {
            q = q.Where(c => c.Origin == origin);
        }

        if (query.BatchNo.HasValue)
        {
            var batchNo = query.BatchNo.Value;
            q = q.Where(c => c.BatchNo == batchNo);
        }
        else
        {
            switch ((query.Scope ?? "all").Trim().ToLowerInvariant())
            {
                case "latest-batch":
                    {
                        var latest = await LatestBatchNoAsync(ct);
                        if (latest == null) return ApiResponse<PagedResult<CertificateListItemDto>>.SuccessResponse(EmptyPage(query));
                        q = q.Where(c => c.BatchNo == latest);
                        break;
                    }
                case "today":
                    {
                        var offset = TimeSpan.FromMinutes(Math.Clamp(query.TzOffsetMinutes ?? 180, -840, 840));
                        var startUtc = (DateTime.UtcNow + offset).Date - offset;
                        var endUtc = startUtc.AddDays(1);
                        // "النهاردة" = الشهادات الجديدة اللي اتكتشفت النهاردة (مش الأرشيف اللي اتفهرس في الـ Baseline).
                        q = q.Where(c => c.Origin == CertificateOrigin.New && c.DiscoveredAt >= startUtc && c.DiscoveredAt < endUtc);
                        break;
                    }
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<CertificateStatus>(query.Status, true, out var status))
        {
            q = q.Where(c => c.Status == status);
        }

        // البحث: اسم الملف، أو رقم اللوحة (حروف + أرقام مع بعض، زي: ابم7263). صيغة واحدة بس.
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            var plate = PlateNormalizer.Normalize(term);
            var searchPlate = plate.Length >= 3;

            // ILIKE '%..%' بيستخدم GIN trigram indexes (IX_certificates_file_name_trgm / _normalized_plate_trgm).
            // الـ Contains(ToLower()) القديمة كانت بتعمل Seq Scan على الجدول كله.
            var namePattern = "%" + EscapeLike(term) + "%";
            var platePattern = "%" + EscapeLike(plate) + "%";

            q = q.Where(c =>
                EF.Functions.ILike(c.FileName, namePattern) ||
                (searchPlate && c.NormalizedPlate != null && EF.Functions.ILike(c.NormalizedPlate, platePattern)));
        }

        var total = await q.CountAsync(ct);

        var rows = await q
            .OrderByDescending(c => c.DiscoveredAt)
            .ThenBy(c => c.FileName)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new
            {
                c.Id, c.FileName, c.PlateLetters, c.PlateDigits, c.NormalizedPlate,
                c.Make, c.Model, c.Color, c.Lessor, c.Status, c.BatchNo, c.DiscoveredAt, c.DriveCreatedAt,
                c.Origin, c.ParentFolderName
            })
            .ToListAsync(ct);

        var items = rows.Select(c => new CertificateListItemDto(
            c.Id, c.FileName, PlateText.Display(c.PlateLetters, c.PlateDigits), c.NormalizedPlate,
            c.Make, c.Model, c.Color, c.Lessor, c.Status.ToString(),
            c.Status == CertificateStatus.NeedsReview, c.BatchNo, c.DiscoveredAt, c.DriveCreatedAt,
            c.Origin.ToString(), c.ParentFolderName)).ToList();

        return ApiResponse<PagedResult<CertificateListItemDto>>.SuccessResponse(new PagedResult<CertificateListItemDto>
        {
            Items = items,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalCount = total
        });
    }

    public async Task<ApiResponse<CertificateDetailDto>> GetAsync(Guid certificateId, CancellationToken ct = default)
    {
        var dto = await LoadDetailAsync(certificateId, ct);
        return dto == null
            ? ApiResponse<CertificateDetailDto>.Fail("الشهادة غير موجودة.", null, 404)
            : ApiResponse<CertificateDetailDto>.SuccessResponse(dto);
    }

    private async Task<CertificateDetailDto?> LoadDetailAsync(Guid certificateId, CancellationToken ct)
    {
        var c = await _db.Certificates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == certificateId && !x.IsDeletedInDrive, ct);
        if (c == null) return null;

        return new CertificateDetailDto(
            c.Id, c.FileName, c.Provider.ToString(), c.PlateLetters, c.PlateDigits,
            PlateText.Display(c.PlateLetters, c.PlateDigits), c.NormalizedPlate,
            c.Make, c.Model, c.Color, c.Lessor, c.Status.ToString(), c.StatusNote,
            c.IsManuallyEdited, !string.IsNullOrEmpty(c.StoragePath), c.BatchNo, c.DiscoveredAt, c.DriveCreatedAt,
            c.Origin.ToString(), c.ParentFolderId, c.ParentFolderName, c.SizeBytes, c.DriveModifiedAt, c.AttemptCount);
    }

    //// =====================================================================
    ////  Batches (بتتحسب وقت الطلب، مفيش جدول للدفعات)
    //// =====================================================================
    //private sealed record BatchRow(int BatchNo, DateTime CreatedAt, int Total, int Extracted, int NeedsReview, int Failed, int Pending);

    //private IQueryable<BatchRow> BatchQuery(IQueryable<Certificate> source) =>
    //    source.GroupBy(c => c.BatchNo).Select(g => new BatchRow(
    //        g.Key,
    //        g.Min(c => c.DiscoveredAt),
    //        g.Count(),
    //        g.Count(c => c.Status == CertificateStatus.Extracted),
    //        g.Count(c => c.Status == CertificateStatus.NeedsReview),
    //        g.Count(c => c.Status == CertificateStatus.Failed),
    //        g.Count(c => c.Status == CertificateStatus.Pending || c.Status == CertificateStatus.Processing)));

    //private static CertificateBatchDto ToDto(BatchRow b) =>
    //    new(b.BatchNo, b.CreatedAt, b.Total, b.Extracted, b.NeedsReview, b.Failed, b.Pending, b.Pending == 0);

    public async Task<ApiResponse<PagedResult<CertificateBatchDto>>> ListBatchesAsync(
    PaginationParams pagination, CancellationToken ct = default)
    {
        // BatchNo = 0 هو الأرشيف التاريخي (فهرسة بس)، مش دفعة.
        var total = await _db.Certificates.AsNoTracking().Where(c => c.BatchNo > 0 && !c.IsDeletedInDrive)
            .Select(c => c.BatchNo).Distinct().CountAsync(ct);

        var rows = await _db.Certificates.AsNoTracking()
            .Where(c => c.BatchNo > 0 && !c.IsDeletedInDrive)
            .GroupBy(c => c.BatchNo)
            .OrderByDescending(g => g.Key)                       // order + page BEFORE projecting
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(g => new
            {
                BatchNo = g.Key,
                CreatedAt = g.Min(c => c.DiscoveredAt),
                Total = g.Count(),
                Extracted = g.Count(c => c.Status == CertificateStatus.Extracted),
                NeedsReview = g.Count(c => c.Status == CertificateStatus.NeedsReview),
                Failed = g.Count(c => c.Status == CertificateStatus.Failed),
                Pending = g.Count(c => c.Status == CertificateStatus.Pending || c.Status == CertificateStatus.Processing)
            })
            .ToListAsync(ct);

        var items = rows.Select(b => new CertificateBatchDto(
            b.BatchNo, b.CreatedAt, b.Total, b.Extracted, b.NeedsReview, b.Failed, b.Pending, b.Pending == 0)).ToList();

        return ApiResponse<PagedResult<CertificateBatchDto>>.SuccessResponse(new PagedResult<CertificateBatchDto>
        {
            Items = items,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize,
            TotalCount = total
        });
    }
    public async Task<ApiResponse<CertificateBatchDto?>> GetLatestBatchAsync(CancellationToken ct = default)
    {
        var latest = await LatestBatchNoAsync(ct);
        if (latest == null) return ApiResponse<CertificateBatchDto?>.SuccessResponse(null);   // no batches yet

        var b = await _db.Certificates.AsNoTracking()
            .Where(c => c.BatchNo == latest && !c.IsDeletedInDrive)
            .GroupBy(c => c.BatchNo)
            .Select(g => new
            {
                BatchNo = g.Key,
                CreatedAt = g.Min(c => c.DiscoveredAt),
                Total = g.Count(),
                Extracted = g.Count(c => c.Status == CertificateStatus.Extracted),
                NeedsReview = g.Count(c => c.Status == CertificateStatus.NeedsReview),
                Failed = g.Count(c => c.Status == CertificateStatus.Failed),
                Pending = g.Count(c => c.Status == CertificateStatus.Pending || c.Status == CertificateStatus.Processing)
            })
            .FirstOrDefaultAsync(ct);

        return ApiResponse<CertificateBatchDto?>.SuccessResponse(b == null ? null : new CertificateBatchDto(
            b.BatchNo, b.CreatedAt, b.Total, b.Extracted, b.NeedsReview, b.Failed, b.Pending, b.Pending == 0));
    }

    // =====================================================================
    //  File (view / download): أي مستخدم مسجّل
    // =====================================================================
    public async Task<(Stream Stream, string FileName)> OpenFileAsync(Guid certificateId, CancellationToken ct = default)
    {
        var cert = await _db.Certificates.AsNoTracking()
            .Where(c => c.Id == certificateId)
            .Select(c => new { c.FileName, c.StoragePath, c.DriveFileId, c.IsDeletedInDrive })
            .FirstOrDefaultAsync(ct);

        if (cert == null) throw new KeyNotFoundException("الشهادة غير موجودة.");
        if (cert.IsDeletedInDrive) throw new KeyNotFoundException("الملف اتمسح من Drive.");

        var fileName = cert.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? cert.FileName : cert.FileName + ".pdf";

        // نسخة محلية قديمة (من قبل التغيير) لو لسه موجودة، غير كده Drive مباشرة.
        if (!string.IsNullOrEmpty(cert.StoragePath))
        {
            try
            {
                var local = await _storage.OpenReadAsync(cert.StoragePath, ct);
                return (local, fileName);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("Local copy missing for certificate {Id}; falling back to Drive", certificateId);
            }
        }

        // Stream مباشر من Drive للمستخدم: من غير حفظ على السيرفر ومن غير تحميل الملف كله في الذاكرة.
        try
        {
            var stream = await _drive.OpenReadAsync(cert.DriveFileId, ct);
            return (stream, fileName);
        }
        catch (DriveFileNotFoundException)
        {
            // اتمسح من Drive قبل ما الـ Reconcile يلحق يلاحظ: نخفيه من دلوقتي.
            await _db.Certificates.Where(c => c.Id == certificateId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.IsDeletedInDrive, true)
                    .SetProperty(c => c.DeletedInDriveAt, DateTime.UtcNow), CancellationToken.None);
            throw new KeyNotFoundException("الملف مش موجود في Drive.");
        }
        catch (DriveAuthException ex)
        {
            throw new KeyNotFoundException("الملف مش متاح دلوقتي: " + ex.Message);
        }
        catch (DriveThrottledException)
        {
            throw new KeyNotFoundException("Drive مشغول دلوقتي (حد الطلبات). جرّب بعد شوية.");
        }
    }

    // =====================================================================
    //  الفرز: بيتحوّل لـ Referral Job حقيقي (نفس معاملة ملفات الإحالة بالظبط)
    // =====================================================================
    public async Task<ApiResponse<CertificateSortResultDto>> CreateSortAsync(
        Guid currentUserId, CertificateSortRequestDto request, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .WithEffectiveGroup()
            .Where(x => x.User.Id == currentUserId)
            .Select(x => new { x.GroupId })
            .FirstOrDefaultAsync(ct);
        if (user == null) return ApiResponse<CertificateSortResultDto>.Fail("المستخدم غير موجود.", null, 404);

        var sortDenied = await _permissions.GetSortDeniedReasonAsync(currentUserId, ct);
        if (sortDenied is not null) return ApiResponse<CertificateSortResultDto>.Fail(sortDenied, null, 403);

        var batchNo = request.BatchNo ?? await LatestBatchNoAsync(ct);
        if (batchNo == null || batchNo <= 0)
            return ApiResponse<CertificateSortResultDto>.Fail("مفيش دفعات شهادات لسه.", null, 404);

        var certs = await _db.Certificates.AsNoTracking()
            .Where(c => c.BatchNo == batchNo && !c.IsDeletedInDrive)
            .OrderBy(c => c.FileName)
            .Select(c => new
            {
                c.FileName, c.Status, c.PlateLetters, c.PlateDigits, c.NormalizedPlate,
                c.Make, c.Model, c.Color, c.Lessor
            })
            .ToListAsync(ct);

        if (certs.Count == 0)
            return ApiResponse<CertificateSortResultDto>.Fail("الدفعة دي مش موجودة.", null, 404);

        var inProgress = certs.Count(c => c.Status is CertificateStatus.Pending or CertificateStatus.Processing);
        if (inProgress > 0)
            return ApiResponse<CertificateSortResultDto>.Fail(
                $"الدفعة لسه بتتعالج ({inProgress} شهادة متبقية). جرّب بعد شوية.", null, 409);

        var skippedFailed = certs.Count(c => c.Status == CertificateStatus.Failed);
        var usable = certs
            .Where(c => c.Status is CertificateStatus.Extracted or CertificateStatus.NeedsReview)
            .ToList();

        // لوحة صالحة = حروف + أرقام. لوحات الشهادات بتتطبّع بنفس Normalize بتاع ملفات البيانات وملف التشيك.
        var withPlate = usable
            .Where(c => !string.IsNullOrEmpty(c.PlateLetters) && !string.IsNullOrEmpty(c.PlateDigits)
                        && !string.IsNullOrEmpty(c.NormalizedPlate))
            .ToList();
        var skippedNoPlate = usable.Count - withPlate.Count;

        if (withPlate.Count == 0)
            return ApiResponse<CertificateSortResultDto>.Fail("مفيش أي شهادة في الدفعة دي ليها لوحة صالحة للفرز.", null, 422);

        var distinct = new Dictionary<string, (string Raw, string? ExtraJson)>();
        var duplicates = 0;
        foreach (var c in withPlate)
        {
            if (distinct.ContainsKey(c.NormalizedPlate!)) { duplicates++; continue; }

            var vehicleType = string.Join(" ", new[] { c.Make, c.Model }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var extra = new Dictionary<string, string?>();
            AddIfNotBlank(extra, ColFileName, c.FileName);
            AddIfNotBlank(extra, ColVehicleType, vehicleType);
            AddIfNotBlank(extra, ColColor, c.Color);
            AddIfNotBlank(extra, ColLessor, c.Lessor);

            distinct[c.NormalizedPlate!] = (
                PlateText.Display(c.PlateLetters, c.PlateDigits)!,
                extra.Count == 0 ? null : JsonSerializer.Serialize(extra));
        }

        var referralJob = new ReferralJob
        {
            GroupId = user.GroupId,
            UserId = currentUserId,
            FilterType = request.FilterType,
            SourceType = SourceType.Certificates,
            SourceFileName = $"شهادات - دفعة {batchNo}",
            TotalReferralPlates = distinct.Count,
            Status = JobStatus.Processing,
            SelectedHistoricalColumnsJson = JsonSerializer.Serialize(Enum.GetValues<HistoricalDataColumn>().ToList()),
            ReferralColumnsJson = JsonSerializer.Serialize(new List<string> { ColFileName, ColVehicleType, ColColor, ColLessor })
        };
        _db.ReferralJobs.Add(referralJob);
        await _db.SaveChangesAsync(ct);

        foreach (var chunk in distinct.Chunk(1000))
        {
            _db.ReferralPlates.AddRange(chunk.Select(kv => new ReferralPlate
            {
                ReferralJobId = referralJob.Id,
                RawPlate = kv.Value.Raw,
                NormalizedPlate = kv.Key,
                Status = ReferralPlateStatus.Unmatched,
                ExtraDataJson = kv.Value.ExtraJson
            }));
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        var jobId = referralJob.Id;
        _jobs.Enqueue<ReferralMatchingJob>(job => job.ProcessAsync(jobId, CancellationToken.None));

        _logger.LogInformation(
            "Certificate batch #{Batch} sent to sorting by user {UserId}: referral job {JobId}, {Count} plate(s)",
            batchNo, currentUserId, jobId, distinct.Count);

        return ApiResponse<CertificateSortResultDto>.SuccessResponse(
            new CertificateSortResultDto(jobId, batchNo.Value, distinct.Count, duplicates, skippedNoPlate, skippedFailed),
            "بدأنا نعالج الفرز في الخلفية.", 202);
    }

    private static void AddIfNotBlank(Dictionary<string, string?> dict, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) dict[key] = value.Trim();
    }

    // =====================================================================
    //  Admin
    // =====================================================================
    public async Task<ApiResponse<CertificateSystemStatusDto>> GetSystemStatusAsync(bool liveCheck, CancellationToken ct = default)
    {
        var state = _drive.GetState();
        var driveState = state.State;
        var email = state.AccountEmail;
        var lastError = state.LastError;

        if (liveCheck && _drive.IsConfigured)
        {
            try
            {
                email = await _drive.CheckConnectionAsync(ct);
                driveState = "Ok";
            }
            catch (DriveAuthException ex)
            {
                driveState = _drive.GetState().State;
                lastError = ex.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex.Message;
            }
        }

        var counts = await _db.Certificates.AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { g.Key, N = g.Count() })
            .ToListAsync(ct);
        int Count(CertificateStatus s) => counts.Where(x => x.Key == s).Select(x => x.N).FirstOrDefault();

        var latest = await LatestBatchNoAsync(ct) ?? 0;
        var sync = await _db.CertificateSyncStates.AsNoTracking().FirstOrDefaultAsync(ct);
        var deleted = await _db.Certificates.CountAsync(c => c.IsDeletedInDrive, ct);

        var hourAgo = DateTime.UtcNow.AddHours(-1);
        var lastHour = await _db.Certificates.AsNoTracking()
            .Where(c => c.ProcessedAt >= hourAgo && c.ExtractionDurationMs != null)
            .GroupBy(_ => 1)
            .Select(g => new { N = g.Count(), Avg = g.Average(c => (double)c.ExtractionDurationMs!.Value) })
            .FirstOrDefaultAsync(ct);

        return ApiResponse<CertificateSystemStatusDto>.SuccessResponse(new CertificateSystemStatusDto(
            _drive.IsConfigured, driveState, email, state.LastSyncAt, state.LastSyncNewFiles, lastError, latest,
            Count(CertificateStatus.Pending), Count(CertificateStatus.Processing), Count(CertificateStatus.Extracted),
            Count(CertificateStatus.NeedsReview), Count(CertificateStatus.Failed),
            Indexed: Count(CertificateStatus.Indexed),
            DeletedInDrive: deleted,
            BaselineStatus: (sync?.BaselineStatus ?? CertificateBaselineStatus.NotStarted).ToString(),
            BaselineFilesSeen: sync?.BaselineFilesSeen ?? 0,
            BaselineStartedAt: sync?.BaselineStartedAt,
            BaselineCompletedAt: sync?.BaselineCompletedAt,
            LastSyncKind: sync?.LastSyncKind,
            LastSyncFilesListed: sync?.LastSyncFilesListed ?? 0,
            LastSyncAlreadyIndexed: sync?.LastSyncAlreadyIndexed ?? 0,
            LastSyncIndexedHistorical: sync?.LastSyncIndexedHistorical ?? 0,
            LastSyncDriveRequests: sync?.LastSyncDriveRequests ?? 0,
            LastSyncDurationMs: sync?.LastSyncDurationMs ?? 0,
            TotalDriveRequests: sync?.TotalDriveRequests ?? 0,
            ExtractedLastHour: lastHour?.N ?? 0,
            AvgExtractionMsLastHour: lastHour == null ? null : (int)lastHour.Avg,
            ExtractionPausedUntil: _workGate.TrippedUntil));
    }

    public Task<ApiResponse<bool>> TriggerSyncAsync(CancellationToken ct = default)
    {
        if (!_drive.IsConfigured)
            return Task.FromResult(ApiResponse<bool>.Fail(
                "إعدادات Google Drive ناقصة (ClientId / ClientSecret / RefreshToken).", null, 409));

        _jobs.Enqueue<CertificateSyncJob>(j => j.SyncAsync(CancellationToken.None));
        return Task.FromResult(ApiResponse<bool>.SuccessResponse(true, "بدأت المزامنة في الخلفية.", 202));
    }

    public Task<ApiResponse<bool>> TriggerReconcileAsync(CancellationToken ct = default)
    {
        if (!_drive.IsConfigured)
            return Task.FromResult(ApiResponse<bool>.Fail(
                "إعدادات Google Drive ناقصة (ClientId / ClientSecret / RefreshToken).", null, 409));

        _jobs.Enqueue<CertificateSyncJob>(j => j.ReconcileAsync(CancellationToken.None));
        return Task.FromResult(ApiResponse<bool>.SuccessResponse(true, "بدأ الـ Reconcile في الخلفية.", 202));
    }

    public async Task<ApiResponse<DriveProbeDto>> ProbeDriveAsync(CancellationToken ct = default)
    {
        if (!_drive.IsConfigured)
            return ApiResponse<DriveProbeDto>.SuccessResponse(new DriveProbeDto(
                false, null, false, 0, false, 0, new List<string>(), "إعدادات Google Drive ناقصة."));
        try
        {
            var p = await _drive.ProbeAsync(ct);
            return ApiResponse<DriveProbeDto>.SuccessResponse(new DriveProbeDto(
                true, p.AccountEmail, p.ChangesTokenOk, p.FlatPdfFirstPageCount, p.FlatPdfHasMorePages,
                p.SharedWithMeTopLevelCount, p.SampleFileNames, null));
        }
        catch (Exception ex) when (ex is DriveAuthException or DriveThrottledException)
        {
            return ApiResponse<DriveProbeDto>.SuccessResponse(new DriveProbeDto(
                true, null, false, 0, false, 0, new List<string>(), ex.Message));
        }
    }

    public async Task<ApiResponse<RetryResultDto>> RetryAsync(Guid certificateId, bool force = false, CancellationToken ct = default)
    {
        var cert = await _db.Certificates.AsNoTracking()
            .Where(c => c.Id == certificateId)
            .Select(c => new { c.Status, c.Origin, c.IsManuallyEdited, c.IsDeletedInDrive })
            .FirstOrDefaultAsync(ct);

        if (cert == null || cert.IsDeletedInDrive) return ApiResponse<RetryResultDto>.Fail("الشهادة غير موجودة.", null, 404);
        if (cert.Status == CertificateStatus.Processing)
            return ApiResponse<RetryResultDto>.Fail("الشهادة قيد المعالجة دلوقتي.", null, 409);
        if (cert.Origin == CertificateOrigin.Historical)
            return ApiResponse<RetryResultDto>.Fail("دي شهادة من الأرشيف التاريخي (فهرسة بس) ومفيهاش استخراج.", null, 409);
        // إعادة الاستخراج بتكتب فوق التعديل اليدوي: لازم تأكيد صريح.
        if (cert.IsManuallyEdited && !force)
            return ApiResponse<RetryResultDto>.Fail(
                "الشهادة اتعدّلت يدوي وإعادة الاستخراج هتمسح التعديل. ابعت force=true لو عايز كده فعلاً.", null, 409);

        await RequeueAsync(new List<Guid> { certificateId }, ct);
        return ApiResponse<RetryResultDto>.SuccessResponse(new RetryResultDto(1), "اتضافت للمعالجة من جديد.");
    }

    public async Task<ApiResponse<RetryResultDto>> RetryFailedAsync(bool includeStuck, CancellationToken ct = default)
    {
        // Failed دايماً. وبشكل اختياري: Pending/Processing عالقين من أكتر من ساعتين (مثلاً بعد إعادة تشغيل السيرفر).
        // Pending طبيعي (ده الـ queue) فمش بنعتبره عالق. العالق الحقيقي = Processing من أكتر من ساعتين.
        var stuckBefore = DateTime.UtcNow.AddHours(-2);
        var ids = await _db.Certificates.AsNoTracking()
            .Where(c => c.Origin == CertificateOrigin.New && !c.IsDeletedInDrive && !c.IsManuallyEdited &&
                        (c.Status == CertificateStatus.Failed ||
                         (includeStuck && c.Status == CertificateStatus.Processing
                          && (c.ProcessingStartedAt == null || c.ProcessingStartedAt < stuckBefore))))
            .Select(c => c.Id)
            .Take(5000)
            .ToListAsync(ct);

        if (ids.Count == 0)
            return ApiResponse<RetryResultDto>.SuccessResponse(new RetryResultDto(0), "مفيش شهادات فاشلة.");

        await RequeueAsync(ids, ct);
        return ApiResponse<RetryResultDto>.SuccessResponse(new RetryResultDto(ids.Count), "اتضافت للمعالجة من جديد.");
    }

    private async Task RequeueAsync(List<Guid> ids, CancellationToken ct)
    {
        foreach (var part in ids.Chunk(1000))
        {
            var chunk = part.ToList();
            await _db.Certificates.Where(c => chunk.Contains(c.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, CertificateStatus.Pending)
                    .SetProperty(c => c.StatusNote, (string?)null)
                    .SetProperty(c => c.AttemptCount, 0)
                    .SetProperty(c => c.AvailableAt, (DateTime?)null)
                    .SetProperty(c => c.ProcessingStartedAt, (DateTime?)null), ct);
        }

        // الـ DB هو الـ queue: مفيش Job لكل شهادة. نصحّي الـ worker بس.
        _workGate.Signal();
    }

    public async Task<ApiResponse<CertificateDetailDto>> UpdateAsync(
        Guid certificateId, UpdateCertificateDto dto, CancellationToken ct = default)
    {
        var cert = await _db.Certificates.FirstOrDefaultAsync(c => c.Id == certificateId && !c.IsDeletedInDrive, ct);
        if (cert == null) return ApiResponse<CertificateDetailDto>.Fail("الشهادة غير موجودة.", null, 404);
        if (cert.Status == CertificateStatus.Processing)
            return ApiResponse<CertificateDetailDto>.Fail("الشهادة قيد المعالجة دلوقتي.", null, 409);

        var plateChanged = false;

        if (dto.PlateLetters != null)
        {
            var letters = Regex.Replace(dto.PlateLetters, @"\s+", "");
            if (!LettersPattern.IsMatch(letters))
                return ApiResponse<CertificateDetailDto>.Fail("حروف اللوحة لازم تكون من 1 إلى 3 حروف عربية.");
            cert.PlateLetters = letters;
            plateChanged = true;
        }

        if (dto.PlateDigits != null)
        {
            var digits = dto.PlateDigits.Trim();
            if (!DigitsPattern.IsMatch(digits))
                return ApiResponse<CertificateDetailDto>.Fail("أرقام اللوحة لازم تكون من 1 إلى 4 أرقام.");
            cert.PlateDigits = digits;
            plateChanged = true;
        }

        if (plateChanged)
        {
            var all = (cert.PlateLetters ?? "") + (cert.PlateDigits ?? "");
            cert.NormalizedPlate = all.Length == 0 ? null : PlateNormalizer.Normalize(all);
        }

        if (dto.Make != null) cert.Make = TextUtil.NullIfBlank(TextUtil.Truncate(dto.Make.Trim(), 100));
        if (dto.Model != null) cert.Model = TextUtil.NullIfBlank(TextUtil.Truncate(dto.Model.Trim(), 100));
        if (dto.Color != null) cert.Color = TextUtil.NullIfBlank(TextUtil.Truncate(dto.Color.Trim(), 100));
        if (dto.Lessor != null) cert.Lessor = TextUtil.NullIfBlank(TextUtil.Truncate(dto.Lessor.Trim(), 200));

        cert.IsManuallyEdited = true;

        // التصحيح اليدوي بيشيل علامة "محتاجة مراجعة" لو اللوحة كاملة
        if (cert.Status is CertificateStatus.NeedsReview or CertificateStatus.Failed
            && !string.IsNullOrEmpty(cert.PlateLetters) && !string.IsNullOrEmpty(cert.PlateDigits))
        {
            cert.Status = CertificateStatus.Extracted;
            cert.StatusNote = null;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Certificate {Id} edited manually", certificateId);

        var detail = await LoadDetailAsync(certificateId, ct);
        return ApiResponse<CertificateDetailDto>.SuccessResponse(detail!, "تم الحفظ.");
    }

    // =====================================================================
    //  Helpers
    // =====================================================================
    private static PagedResult<CertificateListItemDto> EmptyPage(PaginationParams p) =>
        new() { Items = new List<CertificateListItemDto>(), PageNumber = p.PageNumber, PageSize = p.PageSize, TotalCount = 0 };
}
