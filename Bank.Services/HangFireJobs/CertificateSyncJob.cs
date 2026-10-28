using System.Globalization;
using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Helpers;
using Bank.Services.Interfaces.ICertificate;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.HangFireJobs;

/// <summary>
/// اكتشاف الشهادات: بيزحف على كل "Shared with me" في الـ Drive الـ global، ويقارن بالـ DB على أساس DriveFileId،
/// وأي ملف جديد بيتسجّل في دفعة جديدة (BatchNo) + Job استخراج لكل ملف.
/// DisableConcurrentExecution: مزامنة واحدة بس في نفس الوقت (جدولة أو يدوي).
/// </summary>
[Queue("certificates")]
public class CertificateSyncJob
{
    private const int InsertChunkSize = 500;

    private readonly AppDbContext _db;
    private readonly IGoogleDriveClient _drive;
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CertificateSyncJob> _logger;

    public CertificateSyncJob(
        AppDbContext db,
        IGoogleDriveClient drive,
        IBackgroundJobClient jobs,
        IConfiguration configuration,
        ILogger<CertificateSyncJob> logger)
    {
        _db = db;
        _drive = drive;
        _jobs = jobs;
        _configuration = configuration;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task SyncAsync(CancellationToken ct)
    {
        if (!_drive.IsConfigured)
        {
            _logger.LogWarning("Certificate sync skipped: GoogleDrive settings are not configured");
            return;
        }

        List<DriveFileInfo> files;
        try
        {
            files = await _drive.ListAllSharedPdfsAsync(ct);
        }
        catch (DriveAuthException ex)
        {
            _logger.LogWarning(ex, "Certificate sync aborted: Drive auth problem");
            _drive.ReportSync(0, ex.Message);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Certificate sync failed while listing Drive files");
            _drive.ReportSync(0, ex.Message);
            return;
        }

        // اختياري: تجاهل الملفات الأقدم من تاريخ معيّن (عشان أول مزامنة ماتستورّدش أرشيف قديم كله)
        if (DateTime.TryParse(_configuration["Certificates:SyncFromDate"], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var from))
        {
            files = files.Where(f => f.CreatedAt == null || f.CreatedAt >= from).ToList();
        }

        var known = (await _db.Certificates.AsNoTracking().Select(c => c.DriveFileId).ToListAsync(ct)).ToHashSet();
        var fresh = files.Where(f => known.Add(f.FileId)).ToList();

        if (fresh.Count == 0)
        {
            _drive.ReportSync(0, null);
            _logger.LogInformation("Certificate sync: nothing new ({Listed} files listed)", files.Count);
            return;
        }

        var batchNo = (await _db.Certificates.MaxAsync(c => (int?)c.BatchNo, ct) ?? 0) + 1;
        var ids = new List<Guid>(fresh.Count);

        foreach (var chunk in fresh.Chunk(InsertChunkSize))
        {
            var rows = chunk.Select(f => new Certificate
            {
                DriveFileId = f.FileId,
                FileName = TextUtil.Truncate(f.Name, 500)!,
                BatchNo = batchNo,
                Status = CertificateStatus.Pending,
                DriveCreatedAt = f.CreatedAt ?? DateTime.UtcNow
            }).ToList();

            _db.Certificates.AddRange(rows);
            await _db.SaveChangesAsync(ct);
            ids.AddRange(rows.Select(r => r.Id));
            _db.ChangeTracker.Clear();
        }

        foreach (var id in ids)
        {
            var certId = id;
            _jobs.Enqueue<CertificateExtractionJob>(j => j.ExtractAsync(certId, CancellationToken.None));
        }

        _drive.ReportSync(fresh.Count, null);
        _logger.LogInformation(
            "Certificate sync: batch #{Batch} created with {Count} new file(s) ({Listed} listed in Drive)",
            batchNo, fresh.Count, files.Count);
    }
}
