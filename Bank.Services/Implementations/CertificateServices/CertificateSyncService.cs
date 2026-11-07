using System.Diagnostics;
using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Interfaces.ICertificate;
using Bank.Shared.Enums;
using Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// اكتشاف الملفات بدون زحف على المجلدات:
///  1) Baseline (مرة واحدة): قايمة مسطّحة لكل PDF => فهرسة فقط (Indexed/Historical)، من غير تحميل ولا استخراج.
///     الـ cursor (nextPageToken) بيتحفظ بعد كل صفحة => استئناف بعد أي انقطاع. أي ملف createdTime بتاعه بعد
///     بداية الـ Baseline (أو بعد Certificates:ExtractFromDate) بيتسجّل Pending/New وبيدخل خط الاستخراج.
///  2) Incremental (كل دورة): (أ) Scan: createdTime > watermark   (ب) Changes API: rename / move / trash / وصول جديد.
///     الاتنين idempotent، فلو واحد فيهم فاته حاجة التاني بيلقطها.
///  3) Reconcile (يومي): قايمة كاملة مسطّحة تحدّث الأسماء والمجلدات وتعلّم المحذوف (بسقف أمان).
/// الصفحات بتتكتب في الـ DB أول بأول، والـ worker بيتصحّى بعد أول صفحة فيها شهادات جديدة.
/// </summary>
public sealed class CertificateSyncService : ICertificateSyncService
{
    private readonly AppDbContext _db;
    private readonly IGoogleDriveClient _drive;
    private readonly CertificateIndexWriter _writer;
    private readonly CertificateOptions _options;
    private readonly ICertificateWorkGate _gate;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CertificateSyncService> _logger;

    public CertificateSyncService(
        AppDbContext db, IGoogleDriveClient drive, CertificateIndexWriter writer,
        CertificateOptions options, ICertificateWorkGate gate,
        IConfiguration configuration, ILogger<CertificateSyncService> logger)
    {
        _db = db; _drive = drive; _writer = writer; _options = options;
        _gate = gate; _configuration = configuration; _logger = logger;
    }

    /// <summary>سياق دورة واحدة (إحصائيات + رقم الدفعة اللي بتتحجز مرة واحدة أول ما يظهر ملف جديد).</summary>
    private sealed class RunContext
    {
        public required CertificateSyncState State { get; init; }
        public required DateTime SeenAt { get; init; }
        public required bool Touch { get; init; }          // Baseline/Reconcile: حدّث last_seen_at لكل الموجود
        public int? BatchNo { get; set; }
        public int Listed, AlreadyIndexed, IndexedHistorical, NewFiles, Removed, Pages;
    }

    // ============================ Entry points ============================

    public async Task RunAsync(CancellationToken ct)
    {
        if (!_drive.IsConfigured)
        {
            _logger.LogWarning("Certificate sync skipped: GoogleDrive settings are not configured");
            return;
        }

        await using var @lock = await CertificateSyncLock.TryAcquireAsync(_configuration, ct);
        if (@lock == null)
        {
            _logger.LogInformation("Certificate sync skipped: another sync is already running");
            return;
        }

        var state = await GetStateAsync(ct);
        var kind = state.BaselineStatus == CertificateBaselineStatus.Completed ? "Incremental" : "Baseline";
        await ExecuteAsync(state, kind, touch: kind == "Baseline", async ctx =>
        {
            if (kind == "Baseline") await RunBaselineAsync(ctx, ct);
            else
            {
                await RunScanAsync(ctx, ct);
                await RunChangesAsync(ctx, ct);
            }
        }, ct);
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (!_drive.IsConfigured) return;

        await using var @lock = await CertificateSyncLock.TryAcquireAsync(_configuration, ct);
        if (@lock == null)
        {
            _logger.LogInformation("Certificate reconcile skipped: another sync is already running");
            return;
        }

        var state = await GetStateAsync(ct);
        if (state.BaselineStatus != CertificateBaselineStatus.Completed)
        {
            _logger.LogInformation("Certificate reconcile skipped: baseline not completed yet");
            return;
        }

        await ExecuteAsync(state, "Reconcile", touch: true, ctx => RunReconcileAsync(ctx, ct), ct);
    }

    private async Task ExecuteAsync(
        CertificateSyncState state, string kind, bool touch, Func<RunContext, Task> body, CancellationToken ct)
    {
        var ctx = new RunContext { State = state, SeenAt = DateTime.UtcNow, Touch = touch };
        var sw = Stopwatch.StartNew();
        var req0 = _drive.RequestCount;

        state.LastSyncStartedAt = DateTime.UtcNow;
        state.LastSyncKind = kind;
        await SaveStateAsync(state, ct);

        _logger.LogInformation("Certificate sync started: kind={Kind}", kind);
        string? error = null;
        try
        {
            await body(ctx);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error = "اتوقفت (إيقاف التطبيق). هتكمل من آخر نقطة محفوظة.";
            _logger.LogWarning("Certificate sync cancelled; progress is saved and will resume");
        }
        catch (DriveAuthException ex)
        {
            error = ex.Message;
            _logger.LogWarning(ex, "Certificate sync aborted: Drive auth problem");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Certificate sync failed (kind={Kind}); partial progress is kept and next run resumes", kind);
        }
        finally
        {
            sw.Stop();
            var requests = (int)(_drive.RequestCount - req0);
            state.LastSyncFilesListed = ctx.Listed;
            state.LastSyncAlreadyIndexed = ctx.AlreadyIndexed;
            state.LastSyncIndexedHistorical = ctx.IndexedHistorical;
            state.LastSyncNewFiles = ctx.NewFiles;
            state.LastSyncRemoved = ctx.Removed;
            state.LastSyncDriveRequests = requests;
            state.LastSyncDurationMs = (int)sw.ElapsedMilliseconds;
            state.LastSyncCompletedAt = error == null ? DateTime.UtcNow : state.LastSyncCompletedAt;
            state.LastError = error == null ? null : Truncate(error, 2000);
            state.TotalDriveRequests += requests;

            await SaveStateAsync(state, CancellationToken.None);
            _drive.ReportSync(ctx.NewFiles, error);
            CertificateMetrics.SyncDurationMs.Record(sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "Certificate sync finished: kind={Kind} ok={Ok} pages={Pages} listed={Listed} alreadyIndexed={Already} " +
                "indexedHistorical={Historical} newQueued={New} removed={Removed} driveRequests={Requests} throttled={Throttled} durationMs={Ms}",
                kind, error == null, ctx.Pages, ctx.Listed, ctx.AlreadyIndexed, ctx.IndexedHistorical,
                ctx.NewFiles, ctx.Removed, requests, _drive.ThrottledCount, sw.ElapsedMilliseconds);
        }
    }

    // ============================ Baseline ============================

    private async Task RunBaselineAsync(RunContext ctx, CancellationToken ct)
    {
        var s = ctx.State;

        if (s.BaselineStatus == CertificateBaselineStatus.NotStarted || s.BaselineStartedAt == null)
        {
            // الترتيب مهم: ناخد Changes token *قبل* ما نبدأ القايمة. أي ملف يتضاف أثناء الـ Baseline
            // يا بيظهر في القايمة يا في الـ Changes اللي هنقراها بعد كده => مفيش فجوة.
            s.ChangesPageToken = await _drive.GetStartPageTokenAsync(ct);
            s.BaselineStartedAt = DateTime.UtcNow;
            s.BaselineStatus = CertificateBaselineStatus.Running;
            s.BaselinePageToken = null;
            s.BaselinePagesDone = 0;
            s.BaselineFilesSeen = s.BaselineIndexedHistorical = s.BaselineQueuedNew = 0;
            await SaveStateAsync(s, ct);
            _logger.LogInformation("Certificate baseline started at {Start:o}", s.BaselineStartedAt);
        }
        else
        {
            _logger.LogInformation("Certificate baseline resuming from page {Page} ({Seen} files seen so far)",
                s.BaselinePagesDone, s.BaselineFilesSeen);
        }

        var folders = await _drive.RefreshFolderNamesAsync(ct);
        _logger.LogInformation("Certificate baseline: {Folders} folder name(s) cached", folders);

        var token = s.BaselinePageToken;
        while (true)
        {
            DrivePage page;
            try
            {
                page = await _drive.ListPdfPageAsync(null, token, ct);
            }
            catch (GoogleApiException ex) when (token != null && (int)ex.HttpStatusCode is 400 or 404)
            {
                // الـ pageToken المحفوظ اتبطل: نبدأ القايمة من الأول. آمن لأن الفهرسة idempotent (ON CONFLICT).
                _logger.LogWarning(ex, "Saved baseline page token rejected by Drive; restarting listing from the beginning");
                token = null; s.BaselinePageToken = null;
                continue;
            }

            await IndexPageAsync(ctx, page.Files, ct);

            ctx.Pages++;
            s.BaselinePagesDone++;
            s.BaselineFilesSeen += page.Files.Count;
            token = page.NextPageToken;
            s.BaselinePageToken = token;
            await SaveStateAsync(s, ct);

            _logger.LogInformation(
                "Baseline page {Page}: items={Items} pdfs={Pdfs} totalSeen={Seen} historical+={Hist} newQueued+={New}",
                s.BaselinePagesDone, page.RawItems, page.Files.Count, s.BaselineFilesSeen, ctx.IndexedHistorical, ctx.NewFiles);

            if (token == null) break;
        }

        s.BaselineStatus = CertificateBaselineStatus.Completed;
        s.BaselineCompletedAt = DateTime.UtcNow;
        s.BaselinePageToken = null;
        s.ScanWatermark = s.BaselineStartedAt!.Value.AddMinutes(-_options.ScanOverlapMinutes);
        await SaveStateAsync(s, ct);
        _logger.LogInformation("Certificate baseline completed: {Seen} file(s) seen, {Hist} indexed as historical, {New} queued for extraction",
            s.BaselineFilesSeen, s.BaselineIndexedHistorical, s.BaselineQueuedNew);
    }

    // ============================ Incremental: Scan ============================

    private async Task RunScanAsync(RunContext ctx, CancellationToken ct)
    {
        var s = ctx.State;
        var scanStart = DateTime.UtcNow;
        var after = s.ScanWatermark ?? s.BaselineStartedAt?.AddMinutes(-_options.ScanOverlapMinutes);

        string? token = null;
        do
        {
            var page = await _drive.ListPdfPageAsync(after, token, ct);
            await IndexPageAsync(ctx, page.Files, ct);
            ctx.Pages++;
            token = page.NextPageToken;
        } while (token != null);

        // الـ watermark بيتحرّك لبداية الـ Scan (ناقص overlap) بعد ما يخلص بنجاح بس.
        s.ScanWatermark = scanStart.AddMinutes(-_options.ScanOverlapMinutes);
        await SaveStateAsync(s, ct);
    }

    // ============================ Incremental: Changes ============================

    private async Task RunChangesAsync(RunContext ctx, CancellationToken ct)
    {
        var s = ctx.State;

        if (string.IsNullOrEmpty(s.ChangesPageToken))
        {
            s.ChangesPageToken = await _drive.GetStartPageTokenAsync(ct);
            await SaveStateAsync(s, ct);
            return;
        }

        var token = s.ChangesPageToken!;
        while (true)
        {
            DriveChangesPage page;
            try
            {
                page = await _drive.ListChangesPageAsync(token, ct);
            }
            catch (GoogleApiException ex) when ((int)ex.HttpStatusCode is 400 or 404 or 410)
            {
                // token منتهي. نبدأ token جديد؛ الـ Scan والـ Reconcile اليومي بيغطّوا اللي فات.
                _logger.LogWarning(ex, "Drive changes token rejected; resetting. Daily reconcile will cover any gap");
                s.ChangesPageToken = await _drive.GetStartPageTokenAsync(ct);
                await SaveStateAsync(s, ct);
                return;
            }

            var removedIds = page.Changes.Where(c => c.Removed).Select(c => c.FileId).Distinct().ToList();
            if (removedIds.Count > 0)
                ctx.Removed += await _writer.MarkDeletedAsync(removedIds, ct);

            var active = page.Changes.Where(c => !c.Removed && c.File != null).Select(c => c.File!).ToList();
            if (active.Count > 0) await IndexPageAsync(ctx, active, ct);

            ctx.Pages++;
            if (page.NextPageToken != null)
            {
                token = page.NextPageToken;
                s.ChangesPageToken = token;           // نقطة استئناف آمنة (الكتابة idempotent)
                await SaveStateAsync(s, ct);
                continue;
            }

            s.ChangesPageToken = page.NewStartPageToken ?? token;
            await SaveStateAsync(s, ct);
            break;
        }
    }

    // ============================ Reconcile ============================

    private async Task RunReconcileAsync(RunContext ctx, CancellationToken ct)
    {
        var s = ctx.State;
        await _drive.RefreshFolderNamesAsync(ct);

        string? token = null;
        do
        {
            var page = await _drive.ListPdfPageAsync(null, token, ct);
            await IndexPageAsync(ctx, page.Files, ct);
            ctx.Pages++;
            token = page.NextPageToken;
            _logger.LogInformation("Reconcile page {Page}: pdfs={Pdfs} totalListed={Listed}", ctx.Pages, page.Files.Count, ctx.Listed);
        } while (token != null);

        // وصلنا لهنا = القايمة اكتملت من غير أخطاء => اللي ماتشافش بعد SeenAt اتمسح/اتسحب منه الوصول.
        var (candidates, active) = await _writer.CountSweepCandidatesAsync(ctx.SeenAt, ct);
        var cap = Math.Max(50, (int)(active * _options.MaxDeleteFraction));
        if (candidates > cap)
        {
            var msg = $"Reconcile: {candidates} من {active} ملف ماتشافوش في Drive (أكتر من السقف {cap}). " +
                      "مش هعلّمهم محذوفين تلقائياً؛ راجع صلاحيات الحساب المربوط.";
            _logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        var marked = candidates > 0 ? await _writer.SweepMissingAsync(ctx.SeenAt, ct) : 0;
        ctx.Removed += marked;
        s.LastReconcileAt = DateTime.UtcNow;
        s.LastReconcileMarkedDeleted = marked;
        await SaveStateAsync(s, ct);
    }

    // ============================ Shared: page → index ============================

    /// <summary>قاعدة التصنيف الوحيدة: جديد (بيتستخرج) لو اتعمل في Drive بعد بداية الـ Baseline أو من ExtractFromDate.</summary>
    private bool IsIncoming(DriveFileInfo f, CertificateSyncState s) =>
        f.CreatedAt is { } c &&
        ((s.BaselineStartedAt is { } b && c > b) || (_options.ExtractFromDate is { } from && c >= from));

    private async Task IndexPageAsync(RunContext ctx, List<DriveFileInfo> files, CancellationToken ct)
    {
        var distinct = files.Where(f => !f.Trashed).DistinctBy(f => f.FileId).ToList();
        ctx.Listed += distinct.Count;
        CertificateMetrics.FilesListed.Add(distinct.Count);
        if (distinct.Count == 0) return;

        var rows = new List<IndexRow>(distinct.Count);
        foreach (var f in distinct)
        {
            var parentName = f.ParentId == null ? null : await _drive.GetFolderNameAsync(f.ParentId, ct);
            rows.Add(new IndexRow(f.FileId, f.Name, f.CreatedAt, f.ModifiedAt, f.ParentId, parentName, f.Size,
                IsIncoming(f, ctx.State)));
        }

        // رقم الدفعة بيتحجز مرة واحدة في الدورة وأول ما يظهر ملف جديد (كل شهادات الرفعة الواحدة في دفعة واحدة).
        if (ctx.BatchNo == null && rows.Any(r => r.Incoming))
            ctx.BatchNo = (await _db.Certificates.MaxAsync(c => (int?)c.BatchNo, ct) ?? 0) + 1;

        var result = await _writer.UpsertAsync(rows, ctx.SeenAt, ctx.BatchNo ?? 0, ctx.Touch, ct);

        ctx.AlreadyIndexed += result.AlreadyIndexed;
        ctx.IndexedHistorical += result.InsertedHistorical;
        ctx.NewFiles += result.InsertedNew;
        ctx.State.BaselineIndexedHistorical += ctx.State.BaselineStatus == CertificateBaselineStatus.Running ? result.InsertedHistorical : 0;
        ctx.State.BaselineQueuedNew += ctx.State.BaselineStatus == CertificateBaselineStatus.Running ? result.InsertedNew : 0;

        CertificateMetrics.FilesAlreadyIndexed.Add(result.AlreadyIndexed);
        CertificateMetrics.FilesIndexedHistorical.Add(result.InsertedHistorical);
        CertificateMetrics.FilesQueuedNew.Add(result.InsertedNew);

        // أول ما صفحة فيها جديد تتسجّل، الـ worker يبدأ يستخرج من غير ما يستنى باقي الصفحات.
        if (result.InsertedNew > 0) _gate.Signal();
    }

    // ============================ State ============================

    private async Task<CertificateSyncState> GetStateAsync(CancellationToken ct)
    {
        var s = await _db.CertificateSyncStates.FirstOrDefaultAsync(x => x.Id == CertificateSyncState.SingletonId, ct);
        if (s != null) return s;

        s = new CertificateSyncState();
        _db.CertificateSyncStates.Add(s);
        await _db.SaveChangesAsync(ct);
        return s;
    }

    private async Task SaveStateAsync(CertificateSyncState s, CancellationToken ct)
    {
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static string Truncate(string v, int max) => v.Length <= max ? v : v[..max];
}
