namespace Bank.Shared.Enums;

public enum CertificateStatus
{
    Pending = 0,       // شهادة جديدة مستنية الاستخراج (ده الـ queue بتاع الـ worker)
    Processing = 1,    // الـ worker حجزها وبتتحمل وبتتقري
    Extracted = 2,     // البيانات اتطلعت بثقة كافية
    NeedsReview = 3,   // اتطلعت لكن فيها حاجة محتاجة مراجعة (السبب في StatusNote)
    Failed = 4,        // فشل التحميل أو الاستخراج (السبب في StatusNote)
    Indexed = 5        // ملف تاريخي: متفهرس في الـ DB للبحث/العرض/التنزيل بس، ومفيش استخراج ليه
}
