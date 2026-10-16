namespace Bank.Shared.Enums;

public enum CertificateStatus
{
    Pending = 0,       // اتكتشفت في Drive ولسه ماتعالجتش
    Processing = 1,    // بتتحمل وبتتقري
    Extracted = 2,     // البيانات اتطلعت بثقة كافية
    NeedsReview = 3,   // اتطلعت لكن فيها حاجة محتاجة مراجعة (السبب في StatusNote)
    Failed = 4         // فشل التحميل أو الاستخراج (السبب في StatusNote)
}
