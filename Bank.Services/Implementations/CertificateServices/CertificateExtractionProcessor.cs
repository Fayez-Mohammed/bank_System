using System.Diagnostics;
using System.Net;
using Bank.DAL.Context;
using Bank.Services.Helpers;
using Bank.Services.Interfaces.ICertificate;
using Bank.Shared.Enums;
using Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// بيعالج شهادة واحدة اتحجزت (Status=Processing) بواسطة الـ worker: تحميل → استخراج (نفس منطق توثيق/سجل بدون تغيير) → حفظ.
/// عزل الفشل: أي خطأ بيتسجّل على الشهادة نفسها ومش بيوقف باقي الشهادات.
/// الأخطاء المؤقتة (شبكة / 5xx / quota) بترجّع الشهادة Pending بـ backoff لحد MaxAttempts.
/// مشاكل Drive العامة (توكن / quota) بترجّع الشهادة Pending من غير ما تستهلك محاولة وبترمي استثناء عشان الـ worker يوقف مؤقتاً.
/// </summary>
public sealed class CertificateExtractionProcessor
{
    private readonly AppDbContext _db;
    private readonly IGoogleDriveClient _drive;
    private readonly IFileStorageService _storage;
    private readonly ICertificateExtractor _extractor;
    private readonly CertificateOptions _options;
    private readonly ILogger<CertificateExtractionProcessor> _logger;

    public CertificateExtractionProcessor(
        AppDbContext db, IGoogleDriveClient drive, IFileStorageService storage,
        ICertificateExtractor extractor, CertificateOptions options,
        ILogger<CertificateExtractionProcessor> logger)
    {
        _db = db; _drive = drive; _storage = storage; _extractor = extractor; _options = options; _logger = logger;
    }

    public async Task ProcessAsync(Guid certificateId, CancellationToken ct)
    {
        var cert = await _db.Certificates.FirstOrDefaultAsync(c => c.Id == certificateId, ct);
        if (cert == null) return;

        if (cert.Status != CertificateStatus.Processing)
        {
            _logger.LogWarning("Certificate {Id} is {Status}, not Processing; skipping", certificateId, cert.Status);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            // ---------- 1) الملف: نسخة محلية قديمة لو موجودة، وإلا تحميل من Drive ----------
            byte[]? bytes = null;
            if (!string.IsNullOrEmpty(cert.StoragePath))
            {
                try
                {
                    await using var local = await _storage.OpenReadAsync(cert.StoragePath, ct);
                    using var buffer = new MemoryStream();
                    await local.CopyToAsync(buffer, ct);
                    bytes = buffer.ToArray();
                }
                catch (FileNotFoundException)
                {
                    _logger.LogWarning("Local copy missing for certificate {Id}; re-downloading", certificateId);
                    cert.StoragePath = null;
                }
            }

            if (bytes == null)
            {
                using var ms = cert.SizeBytes is > 0 and < 100_000_000
                    ? new MemoryStream((int)cert.SizeBytes.Value)
                    : new MemoryStream();
                await _drive.DownloadAsync(cert.DriveFileId, ms, ct);
                bytes = ms.ToArray();

                if (!LooksLikePdf(bytes))
                    throw new InvalidDataException("الملف اللي نزل من Drive مش PDF.");

                // الافتراضي: مفيش نسخة محلية دايمة (العرض/التنزيل بيتعمل stream من Drive).
                if (_options.KeepLocalCopy)
                {
                    using var toStore = new MemoryStream(bytes);
                    cert.StoragePath = await _storage.SaveAsync(toStore, EnsurePdfName(cert.FileName), "certificates", ct);
                }
            }

            // ---------- 2) الاستخراج (نفس المنطق الحالي) ----------
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
            cert.ProcessedAt = DateTime.UtcNow;
            cert.ExtractionDurationMs = (int)sw.ElapsedMilliseconds;
            cert.AvailableAt = null;

            (r.NeedsReview ? CertificateMetrics.NeedsReview : CertificateMetrics.Extracted).Add(1);
            CertificateMetrics.ExtractionDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // إيقاف التطبيق: نرجّعها Pending عشان تتعاد بعد الـ restart (ومنستهلكش محاولة).
            Release(cert, consumeAttempt: false, delay: null, note: null);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (DriveAuthException ex)
        {
            Release(cert, consumeAttempt: false, delay: TimeSpan.FromMinutes(5), note: "Drive auth: " + ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;      // الـ worker بيوقف مؤقتاً
        }
        catch (DriveThrottledException ex)
        {
            Release(cert, consumeAttempt: false, delay: TimeSpan.FromMinutes(1), note: "Drive quota: " + ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (DriveFileNotFoundException ex)
        {
            cert.Status = CertificateStatus.Failed;
            cert.StatusNote = TextUtil.Truncate(ex.Message, 1000);
            cert.IsDeletedInDrive = true;
            cert.DeletedInDriveAt = DateTime.UtcNow;
            cert.ProcessedAt = DateTime.UtcNow;
            CertificateMetrics.Failed.Add(1);
            _logger.LogWarning("Certificate {Id} ({File}) no longer exists in Drive", certificateId, cert.FileName);
        }
        catch (Exception ex) when (IsTransient(ex) && cert.AttemptCount < _options.MaxAttempts)
        {
            var delay = TimeSpan.FromMinutes(Math.Pow(2, cert.AttemptCount));   // 2, 4, 8 دقيقة
            _logger.LogWarning(ex, "Transient failure for certificate {Id} (attempt {Attempt}/{Max}); retry in {Delay}",
                certificateId, cert.AttemptCount, _options.MaxAttempts, delay);
            Release(cert, consumeAttempt: true, delay, $"محاولة {cert.AttemptCount}/{_options.MaxAttempts}: {ex.Message}");
            CertificateMetrics.Retried.Add(1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for certificate {Id} ({File})", certificateId, cert.FileName);
            cert.Status = CertificateStatus.Failed;
            cert.StatusNote = TextUtil.Truncate(ex.Message, 1000);
            cert.ProcessedAt = DateTime.UtcNow;
            cert.ExtractionDurationMs = (int)sw.ElapsedMilliseconds;
            CertificateMetrics.Failed.Add(1);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static void Release(Bank.DAL.Models.SystemModels.Certificate cert, bool consumeAttempt, TimeSpan? delay, string? note)
    {
        cert.Status = CertificateStatus.Pending;
        cert.ProcessingStartedAt = null;
        cert.AvailableAt = delay.HasValue ? DateTime.UtcNow.Add(delay.Value) : null;
        if (!consumeAttempt && cert.AttemptCount > 0) cert.AttemptCount--;
        cert.StatusNote = note == null ? null : TextUtil.Truncate(note, 1000);
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException or IOException or TimeoutException => true,
        TaskCanceledException => true,                       // timeout (الإلغاء الحقيقي اتعالج فوق)
        GoogleApiException g => (int)g.HttpStatusCode is 429 or 500 or 502 or 503 or 504,
        _ => false
    };

    private static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length > 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46; // %PDF

    private static string EnsurePdfName(string name) =>
        name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
}
