using Microsoft.AspNetCore.Identity;
using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;
using Bank.DAL.Models.SystemModels;

namespace Bank.DAL.Models.BaseModels;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = null!;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? BoundDeviceHash { get; set; }
    public string? BoundDeviceName { get; set; }
    public DateTime? DeviceBoundAt { get; set; }

    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public virtual Group? AdministeredGroup { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public UserPermission? UserPermission { get; set; }
    public ICollection<UserDataFilePermission> DataFilePermissions { get; set; } = new List<UserDataFilePermission>();
    public ICollection<CheckFile> UploadedCheckFiles { get; set; } = new List<CheckFile>();
    public ICollection<RecordingSession> RecordingSessions { get; set; } = new List<RecordingSession>();
    public ICollection<LiveCheckLog> LiveCheckLogs { get; set; } = new List<LiveCheckLog>();
    public ICollection<DataFile> OwnedDataFiles { get; set; } = new List<DataFile>();
    public ICollection<ReferralJob> ReferralJobs { get; set; } = new List<ReferralJob>();
}