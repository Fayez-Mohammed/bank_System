using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Shared.DTOs.AdminDTOs
{
    public class UserPermissionDto
    {
        //public bool CanRecord { get; set; }
        //public bool CanLiveCheck { get; set; }
        //public bool CanManualCheck { get; set; }
        //public bool CanFilter { get; set; }
        //public bool CanUploadCheckFile { get; set; }
        //public bool CanShareResults { get; set; }
        //public bool CanSharePins { get; set; }
        //public bool CanManageFiles { get; set; }
        // public bool CanFilter { get; set; } = false;
        // public bool CanAppendToTeamFiles { get; set; } = false;
        //  public bool CanManageAdminPins { get; set; } = false;
        //  public bool CanToggleFiles { get; set; } = false;
        public bool CanUploadDataFiles { get; set; } = false;
        public bool CanDownload { get; set; } = false;
        public bool CanShare { get; set; } = false;
    
        public bool CanUploadCheckFile { get; set; } = false;

    }

    public class UpdateUserPermissionDto : UserPermissionDto
    {
    }
}
