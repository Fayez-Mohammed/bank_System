using Bank.Shared.Enums;

namespace Bank.Services.Interfaces.ICertificate;

public record CertificateExtractionResult(
    CertificateProvider Provider,
    string PlateLetters,            // بالترتيب المنطقي، مثلاً: رعط
    string? PlateDigits,            // 4428
    string? NormalizedPlate,
    string? Make,
    string? Model,
    string? Color,
    string? Lessor,                 // اسم المؤجر
    bool NeedsReview,
    List<string> ReviewReasons);

public interface ICertificateExtractor
{
    /// <summary>
    /// استخراج محلي بالكامل. بيكتشف الجهة المُصدِرة تلقائياً:
    ///  - سجل (Sijil): PdfPig + OCR محلي (Tesseract) للحقول العربية.
    ///  - توثيق (Tawtheeq): PdfPig بس، من غير OCR.
    /// بيرمي InvalidDataException لو الملف مش شهادة معروفة.
    /// fileName اختياري: Fallback أخير لأرقام اللوحة في توثيق لو النص ما فيهاش أرقام.
    /// </summary>
    Task<CertificateExtractionResult> ExtractAsync(byte[] pdfBytes, string? fileName = null, CancellationToken ct = default);
}
