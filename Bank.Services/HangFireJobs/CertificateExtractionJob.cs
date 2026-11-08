using System.Diagnostics;
using System.Net;
using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Helpers;
using Bank.Services.Implementations.CertificateServices;
using Bank.Services.Interfaces.ICertificate;
using Bank.Shared.Enums;
using Google;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.HangFireJobs;

/// <summary>
/// استخراج الشهادات الجديدة. الطابور هو جدول certificates (Pending)، والـ jobs هنا بس "عمّال" بيسحبوا منه:
///  - DrainAsync: يسحب شهادة → يحمّلها من Drive (في الذاكرة) → يستخرج → يحفظ، ويكرّر لحد ما الطابور يفضى أو الوقت يخلص.
///    عدد الـ drains اللي شغالة مع بعض = WorkerCount بتاع Hangfire server الاستخراج (Certificates:ExtractionWorkers).
///  - PumpAsync (كل دقيقة): يرجّع الشهادات اللي الـ worker بتاعها مات، ويصحّي drains لو في شغل.
///  - ExtractAsync(id): الشكل القديم (job لكل شهادة) — لسه شغال عشان أي jobs قديمة في Hangfire تخلص صح.
/// شهادة واحدة فشلت مابتوقفش الباقي: كل شهادة ليها try/catch وحالتها بتتسجّل عليها.
/// </summary>
[Queue(CertificateExtractionQueue.QueueName)]
public class CertificateExtractionJob
{
    private readonly AppDbContext _db;
    private readonly IGoogleDriveClient _drive;
    private readonly IFileStorageService _storage;
    private readonly ICertificateExtractor _extractor;
    private readonly CertificateExtractionQueue _queue;
    private readonly ILogger<CertificateExtractionJob> _logger;
    private readonly bool _keepLocalCopy;
    private readonly long _maxPdfBytes;
    private readonly TimeSpan _drainBudget;

    public CertificateExtractionJob(
        AppDbContext db,
        IGoogleDriveClient drive,
        IFileStorageService storage,
        ICertificateExtractor extractor,
        CertificateExtractionQueue queue,
        IConfiguration configuration,
        ILogger<CertificateExtractionJob> logger)
    {
        _db = db;
        _drive = drive;
        _storage = storage;
        _extractor = extractor;
        _queue = queue;
        _logger = logger;
        _keepLocalCopy = bool.TryParse(configuration["Certificates:KeepLocalCopy"], out var keep) && keep;
        _maxPdfBytes = CertificateExtractionQueue.ReadInt(configuration, "Certificates:MaxPdfSizeMb", 50, 1, 1024) * 1024L * 1024L;
        _drainBudget = TimeSpan.FromMinutes(
            CertificateExtractionQueue.ReadInt(configuration, "Certificates:DrainBudgetMinutes", 10, 1, 25));
    }

    private enum Outcome { Done, RetryLater, DriveUnavailable }

    // ============================ Drain ============================

    /// <summary>
    /// Worker واحد: يسحب ويعالج لحد ما الطابور يفضى أو الـ budget يخلص (أقل من InvisibilityTimeout بتاع Hangfire،
    /// عشان Hangfire مايفتكرش إن الـ job مات ويشغّله مرتين). لو لسه في شغل بيصحّي drain جديد مكانه.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task DrainAsync(CancellationToken ct)
    {
        CertificateExtractionQueue.DrainStarted();
        var sw = Stopwatch.StartNew();
        int processed = 0, failed = 0;
        var stoppedForDrive = false;
        try
        {
            while (sw.Elapsed < _drainBudget)
            {
                ct.ThrowIfCancellationRequested();
                var id = await _queue.ClaimNextAsync(ct);
                if (id == null) break;

                var outcome = await ProcessClaimedAsync(id.Value, ct);
                processed++;
                if (outcome == Outcome.RetryLater) failed++;
                if (outcome == Outcome.DriveUnavailable)
                {
                    stoppedForDrive = true;   // Drive واقع (auth): مافيش فايدة نكمّل ونحرق محاولات كل الطابور
                    break;
                }
            }
        }
        finally
        {
            CertificateExtractionQueue.DrainFinished();
        }

        if (processed > 0)
            _logger.LogInformation(
                "Certificate drain finished: processed={Processed} retryLater={RetryLater} elapsed={ElapsedMs}ms stoppedForDrive={Stopped}",
                processed, failed, sw.ElapsedMilliseconds, stoppedForDrive);

        if (!stoppedForDrive && sw.Elapsed >= _drainBudget)
            await _queue.KickAsync(CancellationToken.None);   // لسه في شغل: نسلّم لـ drain جديد
    }

    /// <summary>كل دقيقة: استرجاع الـ leases اللي خلصت + تصحية drains لو في Pending مستني (مثلاً بعد restart أو retry).</summary>
    [AutomaticRetry(Attempts = 0)]
    [Queue("certificates-sync")]
    public async Task PumpAsync(CancellationToken ct)
    {
        await _queue.RecoverExpiredLeasesAsync(ct);
        await _queue.KickAsync(ct);
    }

    /// <summary>الشكل القديم: job لشهادة واحدة. بيسحبها بنفس السحب الذرّي، فمستحيل تتعالج مرتين.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task ExtractAsync(Guid certificateId, CancellationToken ct)
    {
        if (!await _queue.ClaimByIdAsync(certificateId, ct))
        {
            _logger.LogInformation("Certificate {Id} is not claimable (already processed / in progress / removed); skipping",
                certificateId);
            return;
        }

        await ProcessClaimedAsync(certificateId, ct);
    }

    // ============================ One certificate ============================

    private async Task<Outcome> ProcessClaimedAsync(Guid certificateId, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var cert = await _db.Certificates.FirstOrDefaultAsync(c => c.Id == certificateId, ct);
        if (cert == null) return Outcome.Done;

        var total = Stopwatch.StartNew();
        long downloadMs = 0;
        var outcome = Outcome.Done;
        try
        {
            // ---------- 1) الملف: نسخة محلية قديمة لو موجودة، وإلا تحميل من Drive في الذاكرة ----------
            byte[]? bytes = await TryReadLocalCopyAsync(cert, ct);

            if (bytes == null)
            {
                var dl = Stopwatch.StartNew();
                using var ms = new MemoryStream();
                await _drive.DownloadAsync(cert.DriveFileId, ms, ct, _maxPdfBytes);
                bytes = ms.ToArray();
                downloadMs = dl.ElapsedMilliseconds;
                CertificateMetrics.DownloadDurationMs.Record(downloadMs);

                if (!LooksLikePdf(bytes))
                    throw new InvalidDataException("الملف اللي نزل من Drive مش PDF.");

                if (_keepLocalCopy)
                {
                    using var toStore = new MemoryStream(bytes);
                    cert.StoragePath = await _storage.SaveAsync(toStore, EnsurePdfName(cert.FileName), "certificates", ct);
                }
            }

            // ---------- 2) الاستخراج (نفس المنطق بالظبط: سجل OCR / توثيق نصي) ----------
            var r = await _extractor.ExtractAsync(bytes, cert.FileName, ct);

            cert.Provider = r.Provider;
            cert.PlateLetters = TextUtil.Truncate(r.PlateLetters, 10);
            cert.PlateDigits = TextUtil.Truncate(r.PlateDigits, 10);
            cert.NormalizedPlate = TextUtil.Truncate(r.NormalizedPlate, 30);
            cert.Make = TextUtil.Truncate(r.Make, 100);
            cert.Model = TextUtil.Truncate(r.Model, 100);
            cert.Color = TextUtil.Truncate(r.Color, 100);
            cert.Lessor = TextUtil.Truncate(r.Lessor, 200);
            cert.Status = r.NeedsReview ? CertificateStatus.NeedsReview : CertificateStatus.Extracted;
            cert.StatusNote = r.NeedsReview ? TextUtil.Truncate(string.Join(" | ", r.ReviewReasons), 1000) : null;
            cert.ExtractedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // إيقاف التطبيق: نرجّعها Pending من غير ما نحسب المحاولة
            cert.Status = CertificateStatus.Pending;
            cert.ExtractionAttempts = Math.Max(0, cert.ExtractionAttempts - 1);
            cert.ProcessingStartedAt = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (DriveAuthException ex)
        {
            // مشكلة في ربط Drive نفسه (مش في الملف): نأجّلها من غير ما نحسب المحاولة
            _logger.LogWarning("Extraction postponed for certificate {Id}: Drive auth problem ({Error})", certificateId, ex.Message);
            cert.Status = CertificateStatus.Pending;
            cert.ExtractionAttempts = Math.Max(0, cert.ExtractionAttempts - 1);
            cert.NextAttemptAt = DateTime.UtcNow.AddMinutes(10);
            cert.StatusNote = TextUtil.Truncate("Drive: " + ex.Message, 1000);
            outcome = Outcome.DriveUnavailable;
        }
        catch (DriveFileNotFoundException ex)
        {
            _logger.LogWarning("Certificate {Id} ({File}) no longer exists in Drive", certificateId, cert.FileName);
            cert.Status = CertificateStatus.Failed;
            cert.StatusNote = TextUtil.Truncate(ex.Message, 1000);
            cert.IsRemovedFromDrive = true;
            cert.RemovedFromDriveAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (IsTransient(ex, ct) && cert.ExtractionAttempts < _queue.MaxAttempts)
        {
            // شبكة / Drive مؤقتاً: محاولة تانية بعد شوية (1، 2، 4، ... دقايق)
            var delay = TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, cert.ExtractionAttempts - 1)));
            _logger.LogWarning(ex,
                "Transient failure for certificate {Id} ({File}), attempt {Attempt}/{Max}; retry in {Delay}",
                certificateId, cert.FileName, cert.ExtractionAttempts, _queue.MaxAttempts, delay);
            cert.Status = CertificateStatus.Pending;
            cert.NextAttemptAt = DateTime.UtcNow + delay;
            cert.StatusNote = TextUtil.Truncate($"محاولة {cert.ExtractionAttempts} فشلت مؤقتاً: {ex.Message}", 1000);
            outcome = Outcome.RetryLater;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for certificate {Id} ({File})", certificateId, cert.FileName);
            cert.Status = CertificateStatus.Failed;
            cert.StatusNote = TextUtil.Truncate(ex.Message, 1000);
        }

        cert.ProcessingStartedAt = null;
        await _db.SaveChangesAsync(CancellationToken.None);

        total.Stop();
        CertificateMetrics.ExtractionDurationMs.Record(total.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("status", cert.Status.ToString()));
        CertificateMetrics.ExtractionCompleted.Add(1, new KeyValuePair<string, object?>("status", cert.Status.ToString()));
        _logger.LogInformation(
            "Certificate {Id} processed: status={Status} provider={Provider} attempt={Attempt} downloadMs={DownloadMs} totalMs={TotalMs}",
            certificateId, cert.Status, cert.Provider, cert.ExtractionAttempts, downloadMs, total.ElapsedMilliseconds);
        return outcome;
    }

    private async Task<byte[]?> TryReadLocalCopyAsync(Certificate cert, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cert.StoragePath)) return null;
        try
        {
            await using var local = await _storage.OpenReadAsync(cert.StoragePath, ct);
            using var buffer = new MemoryStream();
            await local.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("Local copy missing for certificate {Id}; re-downloading", cert.Id);
            cert.StoragePath = null;
            return null;
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken ct) => ex switch
    {
        GoogleApiException g => (int)g.HttpStatusCode == 429 || (int)g.HttpStatusCode >= 500,
        HttpRequestException h => h.StatusCode == null || h.StatusCode == (HttpStatusCode)429 || (int)h.StatusCode >= 500,
        OperationCanceledException => !ct.IsCancellationRequested,   // timeout
        IOException => true,
        _ => false
    };

    private static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length > 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46; // %PDF

    private static string EnsurePdfName(string name) =>
        name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
}
