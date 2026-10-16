using Bank.DAL.Context;
using Bank.Services.Helpers;
using Bank.Services.Interfaces.ICertificate;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.HangFireJobs;

/// <summary>
/// بيحمّل PDF واحد من Drive، يحفظ نسخة محلية، ويستخرج بياناته (لوحة / مصنع / موديل / لون / مؤجر).
/// Job لكل شهادة عشان الفشل يكون معزول، وعدد التوازي بيتحدد بعدد محركات OCR (Certificates:OcrEngineCount).
/// </summary>
[Queue("certificates")]
public class CertificateExtractionJob
{
    private readonly AppDbContext _db;
    private readonly IGoogleDriveClient _drive;
    private readonly IFileStorageService _storage;
    private readonly ICertificateExtractor _extractor;
    private readonly ILogger<CertificateExtractionJob> _logger;

    public CertificateExtractionJob(
        AppDbContext db,
        IGoogleDriveClient drive,
        IFileStorageService storage,
        ICertificateExtractor extractor,
        ILogger<CertificateExtractionJob> logger)
    {
        _db = db;
        _drive = drive;
        _storage = storage;
        _extractor = extractor;
        _logger = logger;
    }

    // الفشل بيتسجّل على الشهادة نفسها (Status=Failed) والأدمن يعيد المحاولة؛ مفيش Retry تلقائي عشان مانكرّرش تحميل/OCR تقيل.
    [AutomaticRetry(Attempts = 0)]
    public async Task ExtractAsync(Guid certificateId, CancellationToken ct)
    {
        var cert = await _db.Certificates.FirstOrDefaultAsync(c => c.Id == certificateId, ct);
        if (cert == null) return;

        if (cert.Status is CertificateStatus.Extracted or CertificateStatus.NeedsReview)
        {
            _logger.LogInformation("Certificate {Id} already processed; skipping", certificateId);
            return;
        }

        cert.Status = CertificateStatus.Processing;
        cert.StatusNote = null;
        await _db.SaveChangesAsync(ct);

        try
        {
            // ---------- 1) الملف: نسخة محلية لو موجودة، وإلا تحميل من Drive ----------
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
                using var ms = new MemoryStream();
                await _drive.DownloadAsync(cert.DriveFileId, ms, ct);
                bytes = ms.ToArray();

                if (!LooksLikePdf(bytes))
                    throw new InvalidDataException("الملف اللي نزل من Drive مش PDF.");

                using var toStore = new MemoryStream(bytes);
                cert.StoragePath = await _storage.SaveAsync(toStore, EnsurePdfName(cert.FileName), "certificates", ct);
            }

            // ---------- 2) الاستخراج ----------
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
        }
        catch (OperationCanceledException)
        {
            // إيقاف التطبيق: نرجّعها Pending عشان تتعاد
            cert.Status = CertificateStatus.Pending;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for certificate {Id} ({File})", certificateId, cert.FileName);
            cert.Status = CertificateStatus.Failed;
            cert.StatusNote = TextUtil.Truncate(ex is DriveAuthException ? "Drive: " + ex.Message : ex.Message, 1000);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length > 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46; // %PDF

    private static string EnsurePdfName(string name) =>
        name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
}
