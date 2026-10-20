using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

public class DataFile : BaseEntity
{
    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public Guid OwnerId { get; set; }
    public virtual ApplicationUser Owner { get; set; } = null!;

    public string FileName { get; set; } = null!;           // e.g. مكه.xlsx.2026
    public int RowCount { get; set; }
    public bool IsActiveForFiltering { get; set; } = true;   // ON/OFF toggle
    public string? PasswordHash { get; set; }                // كلمة المرور إن وجدت

    public int SortOrder { get; set; }

    public string StoragePath { get; set; } = null!;
    public string? ImportError { get; set; }

    public DataFileImportStatus ImportStatus { get; set; } = DataFileImportStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<HistoricalPlate> HistoricalPlates { get; set; } = new List<HistoricalPlate>();
    public virtual ICollection<MatchResult> MatchResultsAsSource { get; set; } = new List<MatchResult>();
    public virtual ICollection<UserDataFilePermission> UserPermissions { get; set; } = new List<UserDataFilePermission>();
}