namespace Bank.DAL.Models.SystemModels;

/// <summary>
/// مجلد أو اختصار (shortcut) في Drive. الجدول ده بيخلّي المزامنة تعرف "الملف ده جوه المجلدات المشتركة ولا لأ"
/// من الـ DB على طول، من غير ما تزحف على شجرة المجلدات في كل دورة.
/// النطاق (InScope) = المجلدات المشتركة مع الحساب مباشرة (IsSharedRoot) + كل اللي تحتها + أهداف الاختصارات اللي جوّاها.
/// </summary>
public class CertificateDriveNode
{
    /// <summary>Drive ID (مش Guid).</summary>
    public string Id { get; set; } = null!;
    public CertificateDriveNodeKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }

    /// <summary>للاختصارات بس: الـ ID اللي الاختصار بيشاور عليه.</summary>
    public string? ShortcutTargetId { get; set; }

    /// <summary>متشارك مع الحساب مباشرة (sharedWithMeTime موجود).</summary>
    public bool IsSharedRoot { get; set; }

    /// <summary>بيتحسب من الـ DB (recursive) بعد أي تغيير في شكل الشجرة.</summary>
    public bool InScope { get; set; }

    /// <summary>مجلد دخل النطاق بعد الفهرسة الأولى (اتنقل جوّه / اتشارك جديد): محتاج يتعمله listing مرة واحدة.</summary>
    public bool NeedsScan { get; set; }

    public bool IsRemoved { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum CertificateDriveNodeKind
{
    Folder = 0,
    FolderShortcut = 1,
    FileShortcut = 2
}
