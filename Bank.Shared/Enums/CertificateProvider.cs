namespace Bank.Shared.Enums;

/// <summary>الجهة المُصدِرة للشهادة (القالب بيختلف، وطريقة الاستخراج كمان).</summary>
public enum CertificateProvider
{
    Sijil = 0,      // سجل: النص العربي مشفّر في الـ PDF → PdfPig + OCR محلي
    Tawtheeq = 1    // توثيق: النص العربي مقروء مباشرة → PdfPig بس (من غير OCR)
}
