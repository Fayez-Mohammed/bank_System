using Microsoft.Extensions.Configuration;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// كل إعدادات الشهادات ("Certificates:*" و "GoogleDrive:*" للـ Drive tuning) في مكان واحد، بقيم افتراضية مبنية على الجهاز.
/// السيرفر: 16 vCPU / 64GB. محرك Tesseract (LSTM) بياخد core واحد تقريباً أثناء الـ OCR وحوالي 300-500MB (ara + bitmap بـ 300DPI)،
/// فبنسيب نص الـ cores تقريباً للـ API وPostgres وHangfire والـ matching: الافتراضي = ProcessorCount/2 (من 2 لـ 8).
/// </summary>
public sealed class CertificateOptions
{
    /// <summary>عدد محركات Tesseract في الـ Pool (كل محرك مش thread-safe فبيتحجز لمهمة واحدة).</summary>
    public int OcrEngineCount { get; init; }

    /// <summary>عدد حلقات الاستخراج المتوازية. = المحركات + 2 عشان توثيق (من غير OCR) والتحميل ماينتظروش محرك.</summary>
    public int ExtractionConcurrency { get; init; }

    public bool WorkerEnabled { get; init; }
    public int WorkerPollSeconds { get; init; }
    public int MaxAttempts { get; init; }
    public int StuckProcessingMinutes { get; init; }
    public bool ResetProcessingOnStartup { get; init; }
    public bool KeepLocalCopy { get; init; }

    public int DrivePageSize { get; init; }
    public int DriveMaxRetries { get; init; }
    public string DriveCorpora { get; init; } = "user";
    public int ScanOverlapMinutes { get; init; }
    public DateTime? ExtractFromDate { get; init; }
    public double MaxDeleteFraction { get; init; }

    public static int ResolveOcrEngineCount(IConfiguration c) =>
        int.TryParse(c["Certificates:OcrEngineCount"], out var n) && n > 0
            ? n
            : Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    public static CertificateOptions From(IConfiguration c)
    {
        int Int(string key, int def, int min = 1) =>
            int.TryParse(c[key], out var v) && v >= min ? v : def;
        bool Bool(string key, bool def) =>
            bool.TryParse(c[key], out var v) ? v : def;

        var engines = ResolveOcrEngineCount(c);
        //DateTime? from = DateTime.TryParse(c["Certificates:ExtractFromDate"] ?? c["Certificates:SyncFromDate"],
        //    System.Globalization.CultureInfo.InvariantCulture,
        //    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
        //    out var d) ? d : null;
        DateTime? from = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc);
        return new CertificateOptions
        {
            OcrEngineCount = engines,
            ExtractionConcurrency = Int("Certificates:ExtractionConcurrency", engines + 2),
            WorkerEnabled = Bool("Certificates:WorkerEnabled", true),
            WorkerPollSeconds = Int("Certificates:WorkerPollSeconds", 10),
            MaxAttempts = Int("Certificates:MaxAttempts", 3),
            StuckProcessingMinutes = Int("Certificates:StuckProcessingMinutes", 15),
            ResetProcessingOnStartup = Bool("Certificates:ResetProcessingOnStartup", true),
            KeepLocalCopy = Bool("Certificates:KeepLocalCopy", false),
            DrivePageSize = Int("Certificates:DrivePageSize", 1000, 100),
            DriveMaxRetries = Int("Certificates:DriveMaxRetries", 5),
            DriveCorpora = c["Certificates:DriveCorpora"] ?? "user",
            ScanOverlapMinutes = Int("Certificates:ScanOverlapMinutes", 15, 0),
            ExtractFromDate = from,
            MaxDeleteFraction = double.TryParse(c["Certificates:MaxDeleteFraction"],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0 && f <= 1 ? f : 0.2
        };
    }
}
