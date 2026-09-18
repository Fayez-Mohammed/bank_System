using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

public class UserPermission : BaseEntity
{
    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    //public Guid? GroupId { get; set; }
    //public virtual Group? Group { get; set; }

    //public bool CanFilter { get; set; } = true;
    //public bool CanAppendToTeamFiles { get; set; } = false;
    //public bool CanDownload { get; set; } = true;
    //public bool CanShare { get; set; } = true;
    //public bool CanManageAdminPins { get; set; } = false;
    //public bool CanToggleFiles { get; set; } = false;

    //public bool CanRecord { get; set; }
    //public bool CanLiveCheck { get; set; }
    //public bool CanAddLiveCheckFile { get; set; }
    //public bool CanManualCheck { get; set; }
    public bool CanUploadDataFiles { get; set; } = false;
    public bool CanDownload { get; set; } = false;//its own files only, not team files, team files are managed by group permissions
    public bool CanShare { get; set; } = false;//its own files only, not team files, team files are managed by group permissions

    public bool CanUploadCheckFile { get; set; } = false;


    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
