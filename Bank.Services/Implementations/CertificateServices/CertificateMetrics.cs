using System.Diagnostics.Metrics;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// System.Diagnostics.Metrics (meter: "Bank.Certificates"). بتتشاف بـ: dotnet-counters monitor --counters Bank.Certificates -p &lt;pid&gt;
/// أو أي OpenTelemetry exporter. الأرقام المحفوظة في الـ DB (certificate_sync_state) بتتعرض في GET api/admin/certificates/status.
/// </summary>
public static class CertificateMetrics
{
    public static readonly Meter Meter = new("Bank.Certificates", "1.0");

    public static readonly Counter<long> DriveRequests = Meter.CreateCounter<long>("certificates.drive.requests");
    public static readonly Counter<long> DriveThrottled = Meter.CreateCounter<long>("certificates.drive.throttled");
    public static readonly Counter<long> DriveErrors = Meter.CreateCounter<long>("certificates.drive.errors");

    public static readonly Counter<long> FilesListed = Meter.CreateCounter<long>("certificates.sync.files_listed");
    public static readonly Counter<long> FilesIndexedHistorical = Meter.CreateCounter<long>("certificates.sync.indexed_historical");
    public static readonly Counter<long> FilesQueuedNew = Meter.CreateCounter<long>("certificates.sync.queued_new");
    public static readonly Counter<long> FilesAlreadyIndexed = Meter.CreateCounter<long>("certificates.sync.already_indexed");
    public static readonly Histogram<double> SyncDurationMs = Meter.CreateHistogram<double>("certificates.sync.duration_ms");

    public static readonly Counter<long> Extracted = Meter.CreateCounter<long>("certificates.extraction.extracted");
    public static readonly Counter<long> NeedsReview = Meter.CreateCounter<long>("certificates.extraction.needs_review");
    public static readonly Counter<long> Failed = Meter.CreateCounter<long>("certificates.extraction.failed");
    public static readonly Counter<long> Retried = Meter.CreateCounter<long>("certificates.extraction.retried");
    public static readonly Histogram<double> ExtractionDurationMs = Meter.CreateHistogram<double>("certificates.extraction.duration_ms");
}
