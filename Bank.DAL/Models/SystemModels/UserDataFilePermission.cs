using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

/// <summary>
/// ملفات الداتا اللي الأدمن سمح للمستخدم يفرز عليها (مع CanSort).
/// </summary>
public class UserDataFilePermission : BaseEntity
{
    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public Guid DataFileId { get; set; }
    public virtual DataFile DataFile { get; set; } = null!;

    public Guid GrantedByAdminId { get; set; }
    public virtual ApplicationUser GrantedByAdmin { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
