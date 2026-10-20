using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

/// <summary>
/// صلاحيات المستخدم العادي (Role = User). الأدمن والسوبر أدمن مش بيتقروا من هنا.
/// </summary>
public class UserPermission : BaseEntity
{
    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    // إضافة وحذف ملفات التشييك
    public bool CanManageCheckFiles { get; set; } = false;

    // رفع ملفات الداتا وإدارة (تنزيل/تعديل/إلحاق/حذف) الملفات المملوكة للمستخدم بس
    public bool CanUploadDataFiles { get; set; } = false;

    // إجراء الفرز — بشرط إذن صريح لكل ملف داتا (UserDataFilePermission)
    public bool CanSort { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
