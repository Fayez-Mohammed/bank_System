namespace Bank.DAL.Models.SystemModels;

/// <summary>
/// صف واحد (Id = 1): حالة مزامنة Drive متخزنة في الـ DB، عشان أي إعادة تشغيل تكمّل من مكانها.
///  - الفهرسة الأولى (Baseline): مرحلة + page token بيتحفظوا بعد كل صفحة.
///  - المزامنة التدريجية: Changes API page token (بيتاخد قبل ما الفهرسة الأولى تبدأ، فمفيش ملف بيضيع في النص).
/// </summary>
public class CertificateSyncState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    // ---------------- الفهرسة الأولى ----------------
    public CertificateBaselinePhase BaselinePhase { get; set; } = CertificateBaselinePhase.NotStarted;
    /// <summary>Drive files.list page token للمرحلة الحالية (null = من الأول).</summary>
    public string? BaselinePageToken { get; set; }
    public DateTime? BaselineStartedAt { get; set; }
    public DateTime? BaselineCompletedAt { get; set; }
    public long BaselineNodesListed { get; set; }
    public long BaselineFilesListed { get; set; }
    public long BaselineFilesInScope { get; set; }

    /// <summary>
    /// الحد الفاصل: ملف اتعمل في Drive قبل الوقت ده = قديم (Indexed، من غير استخراج). بعده = جديد (استخراج).
    /// بيتحدد مرة واحدة أول ما الفهرسة الأولى تبدأ (Certificates:ExtractCreatedAfter أو وقت البداية).
    /// </summary>
    public DateTime? ExtractCutoff { get; set; }

    // ---------------- المزامنة التدريجية ----------------
    public string? ChangesPageToken { get; set; }
    public DateTime? LastChangesSyncAt { get; set; }
    /// <summary>آخر مرة الـ reconcile (ملفات اتعملت مؤخراً) خلص بنجاح.</summary>
    public DateTime? ReconcileWatermark { get; set; }

    // ---------------- آخر تشغيل (للمتابعة) ----------------
    public DateTime? LastRunStartedAt { get; set; }
    public DateTime? LastRunFinishedAt { get; set; }
    /// <summary>JSON: عدّادات آخر تشغيل (طلبات Drive، صفحات، ملفات جديدة، ...). بيتحدث بعد كل صفحة.</summary>
    public string? LastRunStatsJson { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public int ConsecutiveFailures { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum CertificateBaselinePhase
{
    NotStarted = 0,
    /// <summary>listing مسطّح لكل المجلدات والاختصارات (بدون زحف مجلد مجلد).</summary>
    Nodes = 1,
    /// <summary>listing مسطّح لكل ملفات الـ PDF.</summary>
    Files = 2,
    Completed = 3
}
