using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

/// <summary>
/// شهادة واحدة = ملف PDF واحد في Drive. الجدول ده هو فهرس كل ملفات الشهادات (القديمة والجديدة) وبيانات الاستخراج مع بعض،
/// عشان البحث والعرض والتنزيل يشتغلوا على نفس الـ Id من غير تكرار بيانات.
///  - الملفات القديمة (Historical): Status = Indexed، BatchNo = 0، مفيش تحميل ولا OCR — بحث بالاسم + عرض/تنزيل من Drive بس.
///  - الملفات الجديدة: Status = Pending → Processing → Extracted / NeedsReview / Failed، وليها BatchNo &gt; 0.
/// الدفعة (Batch) = أرقام BatchNo المتساوية: كل الشهادات الجديدة اللي اتكتشفت في نفس دورة مزامنة.
/// الجدول نفسه هو طابور الاستخراج (Pending = مستنية)، وده اللي بيمنع التكرار ويخلّي الاسترجاع بعد الـ restart مضمون.
/// </summary>
public class Certificate : BaseEntity
{
    public string DriveFileId { get; set; } = null!;
    public string FileName { get; set; } = null!;

    /// <summary>0 = ملف قديم (Indexed) مش تبع أي دفعة.</summary>
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

    /// <summary>نسخة محلية قديمة من الـ PDF (لو موجودة). النسخ الجديدة مش بتتحفظ إلا لو Certificates:KeepLocalCopy = true.</summary>
    public string? StoragePath { get; set; }
    public DateTime DriveCreatedAt { get; set; }
    public bool IsManuallyEdited { get; set; }

    // ---------------- بيانات Drive (للبحث وإدارة الملفات) ----------------

    /// <summary>الـ Drive ID بتاع المجلد الأب (أول parent). بيتحدث مع النقل بين المجلدات.</summary>
    public string? ParentFolderId { get; set; }
    public DateTime? DriveModifiedAt { get; set; }
    public long? FileSize { get; set; }

    /// <summary>الملف اتمسح / اتنقل للسلة / اتنقل برّه المجلدات المشتركة / الصلاحية اتشالت. الصف بيفضل (بياناته المستخرجة مهمة).</summary>
    public bool IsRemovedFromDrive { get; set; }
    public DateTime? RemovedFromDriveAt { get; set; }

    // ---------------- طابور الاستخراج ----------------

    /// <summary>عدد المحاولات (بيزيد مع كل claim). بعد Certificates:MaxExtractionAttempts بتبقى Failed.</summary>
    public int ExtractionAttempts { get; set; }
    /// <summary>وقت ما الـ worker مسك الشهادة (Processing). أقدم من الـ lease = worker مات → بترجع Pending.</summary>
    public DateTime? ProcessingStartedAt { get; set; }
    /// <summary>بعد فشل مؤقت (شبكة / Drive)، المحاولة الجاية مش قبل الوقت ده.</summary>
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ExtractedAt { get; set; }
}
