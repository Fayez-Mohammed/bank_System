using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

public class ReferralJob : BaseEntity
{
    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public FilterType FilterType { get; set; }               // new (فرز جديد), full (فرز كلي)
    public SourceType SourceType { get; set; }                // file_upload, direct_paste
    public string? SourceFileName { get; set; }

    // Password protection for referral file if provided
    public string? PasswordHash { get; set; }

    public int TotalReferralPlates { get; set; }
    public int SkippedPlatesCount { get; set; }               // Plates found in check file and skipped
    public int MatchedPlatesCount { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Processing;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<MatchResult> MatchResults { get; set; } = new List<MatchResult>();

    public virtual ICollection<ReferralPlate> ReferralPlates { get; set; } = new List<ReferralPlate>();
}
