using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

public class Group : BaseEntity
{
    public string Name { get; set; } = null!;

    public Guid? AdminId { get; set; }
    public virtual ApplicationUser? Admin { get; set; }

    public bool IsActive { get; set; } = true;

    public bool ShareAdminPins { get; set; }
    public DateTime? AdminPinsSharedAt { get; set; }

    public Guid? LastSharedReferralJobId { get; set; }
    public virtual ReferralJob? LastSharedReferralJob { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<ApplicationUser> Members { get; set; } = new List<ApplicationUser>();

    public ICollection<CheckFile> CheckFiles { get; set; } = new List<CheckFile>();
    public ICollection<RecordingSession> RecordingSessions { get; set; } = new List<RecordingSession>();
    public ICollection<LiveCheckLog> LiveCheckLogs { get; set; } = new List<LiveCheckLog>();
    public ICollection<DataFile> DataFiles { get; set; } = new List<DataFile>();
    public ICollection<ReferralJob> ReferralJobs { get; set; } = new List<ReferralJob>();
}