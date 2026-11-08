namespace Bank.Shared.Enums;

public enum CertificateStatus
{
    Pending = 0,       // ملف جديد اتكتشف في Drive ومستني الاستخراج
    Processing = 1,    // بيتحمل وبيتقري
    Extracted = 2,     // البيانات اتطلعت بثقة كافية
    NeedsReview = 3,   // اتطلعت لكن فيها حاجة محتاجة مراجعة (السبب في StatusNote)
    Failed = 4,        // فشل التحميل أو الاستخراج (السبب في StatusNote)
    Indexed = 5        // ملف قديم (قبل الحد الفاصل): متفهرس للبحث والعرض والتنزيل بس، من غير استخراج
}
