using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Helpers;
using Bank.Services.Implementations.CertificateServices;
using Bank.Services.Interfaces.ICertificate;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.HangFireJobs;

/// <summary>
/// مزامنة Drive → فهرس الشهادات. مفيش زحف على شجرة المجلدات في أي دورة عادية.
///
/// 1) الفهرسة الأولى (مرة واحدة، بتكمّل من مكانها لو اتقطعت):
///    - Changes token بيتاخد الأول (أي حاجة تتغير أثناء الفهرسة هتيجي بعدين من Changes API).
///    - listing مسطّح لكل المجلدات/الاختصارات (~صفحة لكل 1000) → جدول certificate_drive_nodes → النطاق بيتحسب في SQL.
///    - listing مسطّح لكل الـ PDFs (~400 صفحة لـ 400 ألف ملف) → certificates:
///        اتعمل قبل الحد الفاصل = Indexed (من غير تحميل ولا OCR)، بعده = Pending (استخراج).
///    - الـ page token والمرحلة بيتحفظوا بعد كل صفحة.
/// 2) كل دورة بعد كده:
///    - Changes API من الـ token المحفوظ: ملفات جديدة / اتغير اسمها / اتنقلت / اتمسحت (صفحة صفحة، والـ token بيتحفظ بعد كل صفحة).
///    - Reconcile: query واحدة رخيصة "PDFs اتعملت من آخر مرة" — شبكة أمان لو Changes API فوّت حاجة.
///    - مجلدات دخلت النطاق جديد (اتنقلت جوّه / اتشاركت): listing لمحتواها بس.
/// كل صفحة ملفات جديدة بتدخل طابور الاستخراج فوراً (مش بعد ما المزامنة كلها تخلص).
/// كل تشغيل ليه budget وقت (أقل من InvisibilityTimeout)؛ لو الشغل ماخلصش بيكمّل في تشغيل تاني على طول.
/// </summary>
[Queue(SyncQueue)]
public class CertificateSyncJob
{
    public const string SyncQueue = "certificates-sync";
    private const long LockKey = 0x43455254_53594E43;   // "CERTSYNC"

    private const string NodesQuery =
        "(mimeType = 'application/vnd.google-apps.folder' or mimeType = 'application/vnd.google-apps.shortcut') and trashed = false";
    private const string PdfQuery = "mimeType = 'application/pdf' and trashed = false";

    private const string NoteDeleted = "الملف اتمسح من Drive (أو اتنقل للسلة / الصلاحية اتشالت) قبل الاستخراج.";
    private const string NoteOutOfScope = "الملف اتنقل برّه المجلدات المشتركة قبل الاستخراج.";

    private readonly AppDbContext _db;
    private readonly CertificateIndexStore _store;
    private readonly IGoogleDriveClient _drive;
    private readonly CertificateExtractionQueue _queue;
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CertificateSyncJob> _logger;

    public CertificateSyncJob(
        AppDbContext db,
        CertificateIndexStore store,
        IGoogleDriveClient drive,
        CertificateExtractionQueue queue,
        IBackgroundJobClient jobs,
        IConfiguration configuration,
        ILogger<CertificateSyncJob> logger)
    {
        _db = db;
        _store = store;
        _drive = drive;
        _queue = queue;
        _jobs = jobs;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>عدّادات التشغيل الحالي: بتتسجّل في اللوج بعد كل صفحة وبتتحفظ في certificate_sync_state.</summary>
    private sealed class RunStats
    {
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public string Stage { get; set; } = "starting";
        public int DriveRequests { get; set; }
        public int Pages { get; set; }
        public long ItemsListed { get; set; }
        public int NodesChanged { get; set; }
        public long FilesInScope { get; set; }
        public long FilesOutOfScope { get; set; }
        public int NewPending { get; set; }
        public int NewIndexed { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }
        public int ChangesSeen { get; set; }
        public int ParentsResolved { get; set; }
        public int FoldersScanned { get; set; }
        public int ReconcileNew { get; set; }
        public int ExtractionKicks { get; set; }
        public int? BatchNo { get; set; }
        public bool BudgetExhausted { get; set; }
        public long ElapsedMs { get; set; }
    }

    private sealed class Run
    {
        public required CertificateSyncState State { get; init; }
        public required RunStats Stats { get; init; }
        public required Stopwatch Clock { get; init; }
        public required TimeSpan Budget { get; init; }
        public DateTime Cutoff => State.ExtractCutoff ?? DateTime.UtcNow;
        public bool OutOfTime => Clock.Elapsed >= Budget;
        /// <summary>IDs اتسألنا عنها في التشغيل ده (عشان مانسألش Drive على نفس المجلد مرتين).</summary>
        public HashSet<string> Resolved { get; } = new();
    }

    // =====================================================================
    //  Entry point (recurring + manual)
    // =====================================================================
    [AutomaticRetry(Attempts = 0)]
    public async Task SyncAsync(CancellationToken ct)
    {
        if (!_drive.IsConfigured)
        {
            _logger.LogWarning("Certificate sync skipped: GoogleDrive settings are not configured");
            return;
        }

        if (!await _store.TryAcquireLockAsync(LockKey, ct))
        {
            _logger.LogInformation("Certificate sync skipped: another sync run is still in progress");
            return;
        }

        var budget = TimeSpan.FromMinutes(
            CertificateExtractionQueue.ReadInt(_configuration, "Certificates:SyncRunBudgetMinutes", 20, 1, 25));
        var run = new Run
        {
            State = await _store.LoadStateAsync(ct),
            Stats = new RunStats(),
            Clock = Stopwatch.StartNew(),
            Budget = budget
        };

        try
        {
            run.State.LastRunStartedAt = run.Stats.StartedAt;
            await SaveAsync(run, ct);

            if (run.State.BaselinePhase != CertificateBaselinePhase.Completed)
                await RunBaselineAsync(run, ct);

            // التدريجي بيشتغل بعد أول فهرسة كاملة (حتى لو في إعادة فهرسة شغالة دلوقتي)
            if (run.State.BaselineCompletedAt != null && !run.OutOfTime) await RunChangesAsync(run, ct);
            if (run.State.BaselineCompletedAt != null && !run.OutOfTime) await RunReconcileAsync(run, ct);
            if (run.State.BaselineCompletedAt != null && !run.OutOfTime) await RunFolderScansAsync(run, ct);

            run.Stats.BudgetExhausted = run.OutOfTime;
            run.Stats.Stage = "done";
            run.State.LastRunFinishedAt = DateTime.UtcNow;
            run.State.LastError = null;
            run.State.ConsecutiveFailures = 0;
            await SaveAsync(run, CancellationToken.None);

            _drive.ReportSync(run.Stats.NewPending, null);
            _logger.LogInformation(
                "Certificate sync finished: baseline={Phase} newPending={NewPending} newIndexed={NewIndexed} updated={Updated} " +
                "removed={Removed} changes={Changes} reconcileNew={ReconcileNew} foldersScanned={Scanned} driveRequests={Requests} " +
                "elapsed={ElapsedMs}ms budgetExhausted={Budget}",
                run.State.BaselinePhase, run.Stats.NewPending, run.Stats.NewIndexed, run.Stats.Updated, run.Stats.Removed,
                run.Stats.ChangesSeen, run.Stats.ReconcileNew, run.Stats.FoldersScanned, run.Stats.DriveRequests,
                run.Clock.ElapsedMilliseconds, run.Stats.BudgetExhausted);

            if (run.OutOfTime)
            {
                // لسه في شغل: تشغيل تاني على طول (بيستنى القفل ده يتفك، ومافيش worker محجوز مستني)
                _jobs.Enqueue<CertificateSyncJob>(j => j.SyncAsync(CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Certificate sync cancelled (shutdown); progress up to the last page is saved");
            throw;
        }
        catch (Exception ex)
        {
            var auth = ex is DriveAuthException;
            if (auth) _logger.LogWarning(ex, "Certificate sync aborted: Drive auth problem");
            else _logger.LogError(ex, "Certificate sync failed at stage {Stage}", run.Stats.Stage);

            run.State.LastError = TextUtil.Truncate($"[{run.Stats.Stage}] {ex.Message}", 2000);
            run.State.LastErrorAt = DateTime.UtcNow;
            run.State.ConsecutiveFailures++;
            run.State.LastRunFinishedAt = DateTime.UtcNow;
            try { await SaveAsync(run, CancellationToken.None); }
            catch (Exception saveEx) { _logger.LogError(saveEx, "Could not save certificate sync state after failure"); }
            _drive.ReportSync(run.Stats.NewPending, ex.Message);
        }
        finally
        {
            await _store.ReleaseLockAsync(LockKey);
        }
    }

    // =====================================================================
    //  1) الفهرسة الأولى
    // =====================================================================
    private async Task RunBaselineAsync(Run run, CancellationToken ct)
    {
        var s = run.State;

        if (s.BaselinePhase == CertificateBaselinePhase.NotStarted)
        {
            var now = DateTime.UtcNow;
            s.BaselineStartedAt ??= now;
            s.ExtractCutoff ??= ReadCutoff() ?? now;

            // الـ token قبل أي listing: أي ملف يتضاف/يتغير أثناء الفهرسة هيظهر في Changes بعدين
            if (s.ChangesPageToken == null)
            {
                run.Stats.DriveRequests++;
                s.ChangesPageToken = await _drive.GetStartPageTokenAsync(ct);
            }

            s.BaselinePhase = CertificateBaselinePhase.Nodes;
            s.BaselinePageToken = null;
            await SaveAsync(run, ct);
            _logger.LogInformation(
                "Certificate baseline indexing started: files created before {Cutoff:O} are indexed only, newer files are extracted",
                s.ExtractCutoff);
        }

        // ---- المرحلة 1: كل المجلدات والاختصارات (listing مسطّح) ----
        if (s.BaselinePhase == CertificateBaselinePhase.Nodes)
        {
            run.Stats.Stage = "baseline:nodes";
            while (!run.OutOfTime)
            {
                var page = await ListPageAsync(run, NodesQuery, s.BaselinePageToken, ct);
                if (page == null) { s.BaselinePageToken = null; continue; }   // token انتهى: المرحلة من الأول (idempotent)

                var nodes = page.Items.Where(i => !i.Trashed).Select(CertificateIndexStore.ToNode)
                    .Where(n => n != null).Select(n => n!).ToList();
                run.Stats.NodesChanged += await _store.UpsertNodesAsync(nodes, ct);
                s.BaselineNodesListed += page.Items.Count;
                s.BaselinePageToken = page.NextPageToken;

                if (page.NextPageToken == null)
                {
                    await _store.RecomputeScopeAsync(markNewFoldersForScan: false, ct);
                    s.BaselinePhase = CertificateBaselinePhase.Files;
                    s.BaselinePageToken = null;
                    await SaveAsync(run, ct);
                    _logger.LogInformation("Certificate baseline: {Count} folders/shortcuts indexed, scope computed",
                        s.BaselineNodesListed);
                    break;
                }

                await SaveAsync(run, ct);
                LogProgress(run);
            }
        }

        // ---- المرحلة 2: كل ملفات الـ PDF (listing مسطّح) ----
        if (s.BaselinePhase == CertificateBaselinePhase.Files)
        {
            run.Stats.Stage = "baseline:files";
            while (!run.OutOfTime)
            {
                var page = await ListPageAsync(run, PdfQuery, s.BaselinePageToken, ct);
                if (page == null) { s.BaselinePageToken = null; continue; }

                var (inScope, _) = await FilterInScopeAsync(run, page.Items, markNewFoldersForScan: false, ct);
                await UpsertFilesAsync(run, inScope, insertOnly: false, ct);

                s.BaselineFilesListed += page.Items.Count;
                s.BaselineFilesInScope += inScope.Count;
                s.BaselinePageToken = page.NextPageToken;

                if (page.NextPageToken == null)
                {
                    s.BaselinePhase = CertificateBaselinePhase.Completed;
                    s.BaselineCompletedAt = DateTime.UtcNow;
                    s.BaselinePageToken = null;
                    s.ReconcileWatermark ??= s.BaselineStartedAt;
                    await SaveAsync(run, ct);
                    _logger.LogInformation(
                        "Certificate baseline completed: {Listed} PDFs listed, {InScope} in shared folders, elapsed since start {Elapsed}",
                        s.BaselineFilesListed, s.BaselineFilesInScope, DateTime.UtcNow - s.BaselineStartedAt);
                    break;
                }

                await SaveAsync(run, ct);
                LogProgress(run);
            }
        }
    }

    // =====================================================================
    //  2) Changes API
    // =====================================================================
    private async Task RunChangesAsync(Run run, CancellationToken ct)
    {
        if (!ReadBool("Certificates:UseChangesApi", true)) return;
        var s = run.State;
        run.Stats.Stage = "changes";

        while (!run.OutOfTime)
        {
            if (s.ChangesPageToken == null)
            {
                run.Stats.DriveRequests++;
                s.ChangesPageToken = await _drive.GetStartPageTokenAsync(ct);
                await SaveAsync(run, ct);
                return;
            }

            DriveChangesPage page;
            try
            {
                run.Stats.DriveRequests++;
                page = await _drive.ListChangesPageAsync(s.ChangesPageToken, 1000, ct);
            }
            catch (DrivePageTokenInvalidException ex)
            {
                // الـ token مابقاش صالح: نبدأ token جديد، والـ reconcile بيغطي الملفات الجديدة من آخر مزامنة ناجحة
                _logger.LogWarning(ex, "Drive changes token is no longer valid; starting a new one (reconcile covers the gap)");
                s.ReconcileWatermark = Min(s.ReconcileWatermark, s.LastChangesSyncAt);
                s.ChangesPageToken = null;
                continue;
            }

            run.Stats.Pages++;
            run.Stats.ChangesSeen += page.Changes.Count;
            run.Stats.ItemsListed += page.Changes.Count;
            CertificateMetrics.SyncItemsListed.Add(page.Changes.Count);

            await ApplyChangesAsync(run, page.Changes, ct);

            s.ChangesPageToken = page.NextPageToken ?? page.NewStartPageToken ?? s.ChangesPageToken;
            await SaveAsync(run, ct);
            if (page.Changes.Count > 0) LogProgress(run);

            if (page.NextPageToken == null)
            {
                s.LastChangesSyncAt = DateTime.UtcNow;
                await SaveAsync(run, ct);
                break;
            }
        }
    }

    private async Task ApplyChangesAsync(Run run, IReadOnlyList<DriveChange> changes, CancellationToken ct)
    {
        if (changes.Count == 0) return;

        var gone = changes.Where(c => c.Removed || c.Item == null || c.Item.Trashed).Select(c => c.FileId).ToList();
        var live = changes.Where(c => c.Item != null && !c.Removed && !c.Item.Trashed).Select(c => c.Item!).ToList();

        // 1) الحاجات اللي اتشالت (مجلد أو ملف — بنجرّب الاتنين، الـ UPDATE على ID مش موجود مابيعملش حاجة)
        var topologyChanged = false;
        if (gone.Count > 0)
        {
            topologyChanged |= await _store.MarkNodesRemovedAsync(gone, ct) > 0;
            run.Stats.Removed += await _store.MarkFilesRemovedAsync(gone, NoteDeleted, ct);
        }

        // 2) المجلدات والاختصارات قبل الملفات (عشان نطاق الملفات يتحسب على الشجرة الجديدة)
        var nodes = live.Select(CertificateIndexStore.ToNode).Where(n => n != null).Select(n => n!).ToList();
        if (nodes.Count > 0)
        {
            var changed = await _store.UpsertNodesAsync(nodes, ct);
            run.Stats.NodesChanged += changed;
            topologyChanged |= changed > 0;
        }

        if (topologyChanged)
        {
            var scope = await _store.RecomputeScopeAsync(markNewFoldersForScan: true, ct);
            await RegisterNewFileShortcutsAsync(run, scope.NowInScope, ct);
        }

        // 3) ملفات الـ PDF
        var pdfs = live.Where(i => i.IsPdf).ToList();
        if (pdfs.Count > 0)
        {
            var (inScope, outOfScope) = await FilterInScopeAsync(run, pdfs, markNewFoldersForScan: true, ct);
            await UpsertFilesAsync(run, inScope, insertOnly: false, ct);
            if (outOfScope.Count > 0)
                run.Stats.Removed += await _store.MarkFilesRemovedAsync(outOfScope, NoteOutOfScope, ct);
        }
    }

    // =====================================================================
    //  3) Reconcile: شبكة أمان رخيصة (PDFs اتعملت مؤخراً)
    // =====================================================================
    private async Task RunReconcileAsync(Run run, CancellationToken ct)
    {
        if (!ReadBool("Certificates:ReconcileEnabled", true)) return;
        var s = run.State;
        run.Stats.Stage = "reconcile";

        var overlap = TimeSpan.FromMinutes(
            CertificateExtractionQueue.ReadInt(_configuration, "Certificates:ReconcileOverlapMinutes", 120, 0, 10080));
        var startedAt = DateTime.UtcNow;
        var since = (s.ReconcileWatermark ?? s.BaselineStartedAt ?? startedAt) - overlap;
        var query = $"{PdfQuery} and createdTime > '{since.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}'";

        var newBefore = run.Stats.NewPending + run.Stats.NewIndexed;
        string? token = null;
        do
        {
            if (run.OutOfTime) return;   // الـ watermark مابيتحركش، فالتشغيل الجاي بيعيد نفس الفترة
            var page = await ListPageAsync(run, query, token, ct);
            if (page == null) return;

            var (inScope, _) = await FilterInScopeAsync(run, page.Items, markNewFoldersForScan: true, ct);
            await UpsertFilesAsync(run, inScope, insertOnly: false, ct);
            token = page.NextPageToken;
        } while (token != null);

        run.Stats.ReconcileNew += run.Stats.NewPending + run.Stats.NewIndexed - newBefore;
        if (run.Stats.ReconcileNew > 0 && ReadBool("Certificates:UseChangesApi", true))
        {
            // Changes API اشتغل قبلها في نفس التشغيل، فأي ملف جديد هنا = Changes فوّته (أو اتضاف في الثواني دي)
            _logger.LogWarning(
                "Certificate reconcile found {Count} new file(s) that the Drive changes feed did not report",
                run.Stats.ReconcileNew);
        }

        s.ReconcileWatermark = startedAt;
        await SaveAsync(run, ct);
    }

    // =====================================================================
    //  4) مجلدات دخلت النطاق جديد: listing لمحتواها بس
    // =====================================================================
    private async Task RunFolderScansAsync(Run run, CancellationToken ct)
    {
        run.Stats.Stage = "folder-scans";
        while (!run.OutOfTime)
        {
            var folders = await _store.GetFoldersNeedingScanAsync(20, ct);
            if (folders.Count == 0) return;

            foreach (var folderId in folders)
            {
                if (run.OutOfTime) return;   // NeedsScan لسه true: التشغيل الجاي يكمّل (idempotent)

                var nodesChanged = 0;
                string? token = null;
                do
                {
                    var page = await ListPageAsync(run, $"'{EscapeId(folderId)}' in parents and trashed = false", token, ct);
                    if (page == null) break;

                    var nodes = page.Items.Select(CertificateIndexStore.ToNode).Where(n => n != null).Select(n => n!).ToList();
                    nodesChanged += await _store.UpsertNodesAsync(nodes, ct);

                    var (inScope, _) = await FilterInScopeAsync(run, page.Items.Where(i => i.IsPdf).ToList(),
                        markNewFoldersForScan: true, ct);
                    await UpsertFilesAsync(run, inScope, insertOnly: false, ct);
                    token = page.NextPageToken;
                } while (token != null);

                if (nodesChanged > 0)
                {
                    // المجلدات الفرعية الجديدة بتدخل النطاق هنا وبتتعلّم NeedsScan
                    var scope = await _store.RecomputeScopeAsync(markNewFoldersForScan: true, ct);
                    await RegisterNewFileShortcutsAsync(run, scope.NowInScope, ct);
                }

                await _store.ClearNeedsScanAsync(folderId, ct);
                run.Stats.FoldersScanned++;
                await SaveAsync(run, ct);
            }

            LogProgress(run);
        }
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>صفحة files.list. null = الـ page token مابقاش صالح (الـ caller يبدأ المرحلة من الأول).</summary>
    private async Task<DriveListPage?> ListPageAsync(Run run, string query, string? pageToken, CancellationToken ct)
    {
        try
        {
            run.Stats.DriveRequests++;
            var page = await _drive.ListPageAsync(query, pageToken, 1000, ct);
            run.Stats.Pages++;
            run.Stats.ItemsListed += page.Items.Count;
            CertificateMetrics.SyncItemsListed.Add(page.Items.Count);
            return page;
        }
        catch (DrivePageTokenInvalidException ex)
        {
            _logger.LogWarning(ex, "Drive list page token expired during {Stage}; restarting this listing", run.Stats.Stage);
            return null;
        }
    }

    /// <summary>
    /// الملف جوه النطاق لو: متشارك مباشرة، أو المجلد الأب جوه النطاق، أو عليه اختصار جوه النطاق.
    /// مجلد أب مش معروف (اتعمل بعد الفهرسة / خارج الـ listing): بنسأل Drive عليه (وعلى اللي فوقه) مرة واحدة ونحفظه.
    /// </summary>
    private async Task<(List<IndexedFileRow> InScope, List<string> OutOfScope)> FilterInScopeAsync(
        Run run, IReadOnlyCollection<DriveItem> items, bool markNewFoldersForScan, CancellationToken ct)
    {
        var pdfs = items.Where(i => i.IsPdf && !i.Trashed).ToList();
        var inScope = new List<IndexedFileRow>(pdfs.Count);
        var outOfScope = new List<string>();
        if (pdfs.Count == 0) return (inScope, outOfScope);

        var parentIds = pdfs.Where(p => !p.SharedWithMe && p.ParentId != null).Select(p => p.ParentId!).Distinct().ToList();
        var scope = await _store.GetNodeScopeAsync(parentIds, ct);

        var unknown = parentIds.Where(id => !scope.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            await ResolveNodesAsync(run, unknown, markNewFoldersForScan, ct);
            foreach (var kv in await _store.GetNodeScopeAsync(unknown, ct)) scope[kv.Key] = kv.Value;
        }

        var maybeShortcutTargets = pdfs
            .Where(p => !p.SharedWithMe && !(p.ParentId != null && scope.TryGetValue(p.ParentId, out var inS) && inS))
            .Select(p => p.Id).ToList();
        var shortcutTargets = await _store.GetInScopeShortcutTargetsAsync(maybeShortcutTargets, ct);

        foreach (var p in pdfs)
        {
            var ok = p.SharedWithMe
                     || (p.ParentId != null && scope.TryGetValue(p.ParentId, out var parentInScope) && parentInScope)
                     || shortcutTargets.Contains(p.Id);
            if (ok) inScope.Add(new IndexedFileRow(p.Id, p.Name, p.CreatedAt, p.ModifiedAt, p.ParentId, p.Size));
            else outOfScope.Add(p.Id);
        }

        run.Stats.FilesInScope += inScope.Count;
        run.Stats.FilesOutOfScope += outOfScope.Count;
        return (inScope, outOfScope);
    }

    /// <summary>بيجيب metadata مجلدات مش معروفة (ومن فوقها لحد ما يوصل لحاجة معروفة)، ويعيد حساب النطاق.</summary>
    private async Task ResolveNodesAsync(Run run, List<string> ids, bool markNewFoldersForScan, CancellationToken ct)
    {
        var pending = new Queue<string>(ids.Where(run.Resolved.Add));
        var found = new List<CertificateDriveNode>();
        var unreachable = new List<string>();

        for (var guard = 0; pending.Count > 0 && guard < 5000; guard++)
        {
            ct.ThrowIfCancellationRequested();
            var id = pending.Dequeue();
            run.Stats.DriveRequests++;
            run.Stats.ParentsResolved++;
            var item = await _drive.GetItemAsync(id, ct);
            var node = item == null || item.Trashed ? null : CertificateIndexStore.ToNode(item);
            if (node == null)
            {
                unreachable.Add(id);
                continue;
            }

            found.Add(node);
            if (node.ParentId != null && run.Resolved.Add(node.ParentId))
            {
                var known = await _store.GetNodeScopeAsync(new[] { node.ParentId }, ct);
                if (known.Count == 0) pending.Enqueue(node.ParentId);
            }
        }

        if (found.Count > 0) run.Stats.NodesChanged += await _store.UpsertNodesAsync(found, ct);
        if (unreachable.Count > 0) await _store.InsertUnreachableNodesAsync(unreachable, ct);
        if (found.Count > 0)
        {
            var scope = await _store.RecomputeScopeAsync(markNewFoldersForScan, ct);
            if (markNewFoldersForScan) await RegisterNewFileShortcutsAsync(run, scope.NowInScope, ct);
        }
    }

    /// <summary>اختصار لملف PDF دخل النطاق: بنسجّل الملف الأصلي (insert بس، عشان مجلده الحقيقي مايتغيرش).</summary>
    private async Task RegisterNewFileShortcutsAsync(Run run, IReadOnlyList<string> nowInScope, CancellationToken ct)
    {
        if (nowInScope.Count == 0) return;
        var shortcuts = await _store.GetFileShortcutsAsync(nowInScope, ct);
        if (shortcuts.Count == 0) return;

        var rows = new List<IndexedFileRow>();
        foreach (var (shortcutId, targetId) in shortcuts)
        {
            run.Stats.DriveRequests++;
            var target = await _drive.GetItemAsync(targetId, ct);
            if (target is { IsPdf: true, Trashed: false })
                rows.Add(new IndexedFileRow(target.Id, target.Name, target.CreatedAt, target.ModifiedAt, target.ParentId, target.Size));
            else
                _logger.LogInformation("File shortcut {Shortcut} points to an unreachable target {Target}", shortcutId, targetId);
        }

        await UpsertFilesAsync(run, rows, insertOnly: true, ct);
    }

    private async Task UpsertFilesAsync(Run run, List<IndexedFileRow> rows, bool insertOnly, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var now = DateTime.UtcNow;
        if (run.Stats.BatchNo == null && rows.Any(r => CertificateIndexStore.IsNew(r, run.Cutoff, now)))
            run.Stats.BatchNo = await _store.NextBatchNoAsync(ct);   // دفعة واحدة لكل تشغيل (بتتحجز أول ما يظهر ملف جديد)

        var counts = await _store.UpsertFilesAsync(rows, run.Cutoff, run.Stats.BatchNo ?? 0, insertOnly, ct);
        run.Stats.NewPending += counts.NewPending;
        run.Stats.NewIndexed += counts.NewIndexed;
        run.Stats.Updated += counts.Updated;
        CertificateMetrics.SyncFilesNew.Add(counts.NewPending);
        CertificateMetrics.SyncFilesIndexed.Add(counts.NewIndexed);

        // الشهادات الجديدة بتدخل الاستخراج فوراً، من غير ما تستنى باقي المزامنة
        if (counts.NewPending > 0)
            run.Stats.ExtractionKicks += await _queue.KickAsync(ct);
    }

    private async Task SaveAsync(Run run, CancellationToken ct)
    {
        run.Stats.ElapsedMs = run.Clock.ElapsedMilliseconds;
        run.State.LastRunStatsJson = JsonSerializer.Serialize(run.Stats);
        run.State.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private void LogProgress(Run run)
    {
        var st = run.Stats;
        _logger.LogInformation(
            "Certificate sync progress: stage={Stage} pages={Pages} listed={Listed} inScope={InScope} newPending={NewPending} " +
            "newIndexed={NewIndexed} updated={Updated} removed={Removed} nodesChanged={Nodes} parentsResolved={Parents} " +
            "driveRequests={Requests} elapsed={ElapsedMs}ms",
            st.Stage, st.Pages, st.ItemsListed, st.FilesInScope, st.NewPending, st.NewIndexed, st.Updated, st.Removed,
            st.NodesChanged, st.ParentsResolved, st.DriveRequests, run.Clock.ElapsedMilliseconds);
    }

    private DateTime? ReadCutoff() =>
        DateTime.TryParse(_configuration["Certificates:ExtractCreatedAfter"], CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;

    private bool ReadBool(string key, bool fallback) =>
        bool.TryParse(_configuration[key], out var v) ? v : fallback;

    private static DateTime? Min(DateTime? a, DateTime? b) =>
        a == null ? b : b == null ? a : (a < b ? a : b);

    /// <summary>IDs جوجل حروف وأرقام و - و _ بس. أي حاجة تانية بتتشال عشان مانكسرش الـ query.</summary>
    private static string EscapeId(string id) =>
        new(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
}
