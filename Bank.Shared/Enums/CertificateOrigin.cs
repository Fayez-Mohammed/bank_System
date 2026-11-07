namespace Bank.Shared.Enums;

/// <summary>
/// Historical = كان موجود في Drive قبل أول مزامنة (Baseline): فهرسة بس، من غير تحميل ولا استخراج.
/// New        = اتكتشف بعد الـ Baseline: بيدخل خط الاستخراج.
/// </summary>
public enum CertificateOrigin
{
    New = 0,
    Historical = 1
}
