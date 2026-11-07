using Bank.Services.Interfaces.ICertificate;
using Hangfire;

namespace Bank.Services.HangFireJobs;

/// <summary>
/// غلاف Hangfire رفيع: الجدولة (كل ٥ دقايق) والتشغيل اليدوي. الشغل الحقيقي في CertificateSyncService.
/// مفيش DisableConcurrentExecution هنا: التنسيق بـ Postgres advisory lock جوه الـ service (مزامنة واحدة، والباقي بيتخطّى فوراً
/// بدل ما يتراكم منتظر lock لو الـ Baseline الأول طوّل).
/// </summary>
[Queue("certificates")]
public class CertificateSyncJob
{
    private readonly ICertificateSyncService _sync;

    public CertificateSyncJob(ICertificateSyncService sync) => _sync = sync;

    [AutomaticRetry(Attempts = 0)]
    public Task SyncAsync(CancellationToken ct) => _sync.RunAsync(ct);

    [AutomaticRetry(Attempts = 0)]
    public Task ReconcileAsync(CancellationToken ct) => _sync.ReconcileAsync(ct);
}
