using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;
public class AuditLog : BaseEntityLong
{
    // Nullable for system background events
    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    public AuditActionType ActionType { get; set; }
    public string EntityName { get; set; } = null!;   // e.g. data_files, user_permissions
    public string EntityId { get; set; } = null!;
    public string? OldValues { get; set; }             // JSON string of previous state
    public string? NewValues { get; set; }             // JSON string of modified state
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
