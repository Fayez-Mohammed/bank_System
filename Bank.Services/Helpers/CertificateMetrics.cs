using System.Diagnostics.Metrics;

namespace Bank.Services.Helpers;

/// <summary>
/// Metrics للشهادات (System.Diagnostics.Metrics). تتشاف حيّة من غير أي إعداد بـ:
///   dotnet-counters monitor -p &lt;pid&gt; --counters Bank.Certificates
/// أو أي OpenTelemetry exporter لو اتضاف بعدين. نفس الأرقام بتتسجّل كمان في اللوج وفي certificate_sync_state.
/// </summary>
public static class CertificateMetrics
{
    public const string MeterName = "Bank.Certificates";
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> DriveRequests = Meter.CreateCounter<long>("drive.requests");
    public static readonly Counter<long> DriveRetries = Meter.CreateCounter<long>("drive.retries");
    public static readonly Counter<long> DriveThrottled = Meter.CreateCounter<long>("drive.throttled");
    public static readonly Counter<long> DriveFailures = Meter.CreateCounter<long>("drive.failures");

    public static readonly Counter<long> SyncItemsListed = Meter.CreateCounter<long>("sync.items_listed");
    public static readonly Counter<long> SyncFilesIndexed = Meter.CreateCounter<long>("sync.files_indexed");
    public static readonly Counter<long> SyncFilesNew = Meter.CreateCounter<long>("sync.files_new");
    public static readonly Counter<long> SyncFilesRemoved = Meter.CreateCounter<long>("sync.files_removed");

    public static readonly Counter<long> ExtractionCompleted = Meter.CreateCounter<long>("extraction.completed");
    public static readonly Histogram<double> ExtractionDurationMs = Meter.CreateHistogram<double>("extraction.duration", "ms");
    public static readonly Histogram<double> DownloadDurationMs = Meter.CreateHistogram<double>("extraction.download_duration", "ms");
}
