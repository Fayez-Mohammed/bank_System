using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

/// <summary>
/// شهادة استرداد واحدة = ملف PDF واحد في Drive. الجدول الوحيد في الميزة.
/// الدفعة (Batch) = أرقام BatchNo المتساوية: كل الشهادات اللي اتكتشفت في نفس دورة مزامنة.
/// مفيش تخزين لبيانات المستأجر، ولا لأي حاجة بتتحسب وقت القراءة (العدّادات، العرض، ...).
/// </summary>
public class Certificate : BaseEntity
{
    public string DriveFileId { get; set; } = null!;
    public string FileName { get; set; } = null!;

    public int BatchNo { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public CertificateProvider Provider { get; set; } = CertificateProvider.Sijil;

    /// <summary>حروف اللوحة بالترتيب المنطقي (الحرف الأول = اللي على اليمين). مثال: رعط</summary>
    public string? PlateLetters { get; set; }
    public string? PlateDigits { get; set; }

    /// <summary>PlateNormalizer.Normalize(letters + digits): نفس صيغة لوحات ملفات البيانات وملف التشيك. للبحث والفرز.</summary>
    public string? NormalizedPlate { get; set; }

    public string? Make { get; set; }       // المصنع
    public string? Model { get; set; }      // الموديل
    public string? Color { get; set; }
    public string? Lessor { get; set; }     // اسم المؤجر

    public CertificateStatus Status { get; set; } = CertificateStatus.Pending;

    /// <summary>NeedsReview: أسباب المراجعة | Failed: سبب الفشل.</summary>
    public string? StatusNote { get; set; }

    /// <summary>النسخة المحلية من الـ PDF (للعرض والتنزيل بسرعة ومن غير الاعتماد على Drive).</summary>
    public string? StoragePath { get; set; }
    public DateTime DriveCreatedAt { get; set; }
    public bool IsManuallyEdited { get; set; }
}
