using Bank.DAL.Context;
using Bank.Services.Implementations.CertificateServices;
using Bank.Services.Interfaces.ICertificate;
using Microsoft.EntityFrameworkCore;

namespace Bank.API.BackgroundServices;

/// <summary>
/// خط الاستخراج: الـ DB هو الـ queue (شهادات Status=Pending و Origin=New)، ومفيش Job في Hangfire لكل ملف.
///
/// • N حلقات متوازية (Certificates:ExtractionConcurrency). كل حلقة بتحجز شهادة واحدة بـ
///   UPDATE .. WHERE id = (SELECT .. FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id
///   => مفيش شهادة بتتعالج مرتين حتى مع أكتر من instance/حلقة، ومفيش tail latency زي الـ batches الثابتة.
/// • الـ OCR مقيّد بـ Pool المحركات (Tesseract مش thread-safe)؛ الحلقات الزيادة (+2) بتغطي توثيق (من غير OCR) والتحميل.
/// • الاستئناف بعد restart: الشهادات العالقة Processing بترجع Pending (أو Failed لو استهلكت كل محاولاتها = poison pill).
/// • Circuit breaker: لو Drive رفض (توكن/quota) الحلقات بتوقف دقايق بدل ما تفشّل الدفعة كلها.
/// </summary>
public sealed class CertificateExtractionWorker : BackgroundService
{
    private const string ClaimSql = @"
        UPDATE certificates
           SET status = 'Processing',
               processing_started_at = now(),
               attempt_count = attempt_count + 1,
               status_note = NULL
         WHERE id = (SELECT id FROM certificates
                      WHERE status = 'Pending'
                        AND origin = 'New'
                        AND is_deleted_in_drive = false
                        AND (available_at IS NULL OR available_at <= now())
                      ORDER BY batch_no, drive_created_at, id
                      LIMIT 1
                      FOR UPDATE SKIP LOCKED)
        RETURNING id";

    private readonly IServiceScopeFactory _scopes;
    private readonly CertificateOptions _options;
    private readonly ICertificateWorkGate _gate;
    private readonly ILogger<CertificateExtractionWorker> _logger;

    public CertificateExtractionWorker(
        IServiceScopeFactory scopes, CertificateOptions options,
        ICertificateWorkGate gate, ILogger<CertificateExtractionWorker> logger)
    {
        _scopes = scopes; _options = options; _gate = gate; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            _logger.LogInformation("Certificate extraction worker disabled (Certificates:WorkerEnabled=false)");
            return;
        }

        // نسيب التطبيق يخلّص الـ startup والـ migrations قبل أول استعلام.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation(
            "Certificate extraction worker started: concurrency={Concurrency} ocrEngines={Engines} maxAttempts={Max}",
            _options.ExtractionConcurrency, _options.OcrEngineCount, _options.MaxAttempts);

        try
        {
            await RecoverStuckAsync(startup: true, stoppingToken);

            var tasks = Enumerable.Range(0, _options.ExtractionConcurrency)
                .Select(i => Task.Run(() => RunLoopAsync(i, stoppingToken), stoppingToken))
                .Append(Task.Run(() => RecoveryLoopAsync(stoppingToken), stoppingToken));

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunLoopAsync(int slot, CancellationToken ct)
    {
        var poll = TimeSpan.FromSeconds(_options.WorkerPollSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_gate.TrippedUntil is { } until)
                {
                    var wait = until - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait > poll ? poll : wait, ct);
                    continue;
                }

                using var scope = _scopes.CreateScope();
                var id = await ClaimAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);
                if (id == null)
                {
                    await _gate.WaitForWorkAsync(poll, ct);
                    continue;
                }

                var processor = scope.ServiceProvider.GetRequiredService<CertificateExtractionProcessor>();
                await processor.ProcessAsync(id.Value, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (DriveAuthException ex)
            {
                _gate.Trip(TimeSpan.FromMinutes(5), ex.Message);
                _logger.LogError("Extraction paused 5 min: Drive auth problem ({Message})", ex.Message);
            }
            catch (DriveThrottledException ex)
            {
                _gate.Trip(TimeSpan.FromMinutes(2), ex.Message);
                _logger.LogWarning("Extraction paused 2 min: Drive quota ({Message})", ex.Message);
            }
            catch (Exception ex)
            {
                // أي خطأ غير متوقع (DB مثلاً) مايكسرش الحلقة؛ نستنى شوية ونكمل.
                _logger.LogError(ex, "Extraction loop {Slot} error", slot);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private static async Task<Guid?> ClaimAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var opened = false;
        if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); opened = true; }
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ClaimSql;
            return await cmd.ExecuteScalarAsync(ct) as Guid?;
        }
        finally { if (opened) await conn.CloseAsync(); }
    }

    private async Task RecoveryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); await RecoverStuckAsync(startup: false, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Stuck-certificate recovery failed"); }
        }
    }

    /// <summary>
    /// Processing عالقة (crash / restart / فشل الـ worker): ترجع Pending، إلا لو استهلكت كل محاولاتها (poison pill) فتبقى Failed
    /// عشان ملف بيقتل العملية (OOM مثلاً) ما يلفّش للأبد. عند الـ startup كل Processing بتتعتبر عالقة (instance واحد).
    /// </summary>
    private async Task RecoverStuckAsync(bool startup, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var all = startup && _options.ResetProcessingOnStartup;
        var mins = _options.StuckProcessingMinutes;
        var cond = all
            ? "status = 'Processing'"
            : $"status = 'Processing' AND (processing_started_at IS NULL OR processing_started_at < now() - interval '{mins} minutes')";

        var failed = await db.Database.ExecuteSqlRawAsync($@"
            UPDATE certificates SET status = 'Failed', processing_started_at = NULL,
                   processed_at = now(),
                   status_note = 'فشل بعد {_options.MaxAttempts} محاولات (العملية اتقطعت أثناء المعالجة).'
            WHERE {cond} AND attempt_count >= {_options.MaxAttempts}", ct);

        var requeued = await db.Database.ExecuteSqlRawAsync($@"
            UPDATE certificates SET status = 'Pending', processing_started_at = NULL, available_at = NULL
            WHERE {cond}", ct);

        if (failed + requeued > 0)
        {
            _logger.LogWarning("Recovered stuck certificates: requeued={Requeued} failed={Failed} (startup={Startup})",
                requeued, failed, startup);
            _gate.Signal();
        }
    }
}
