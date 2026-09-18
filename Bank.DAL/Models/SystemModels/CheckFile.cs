using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;


public class CheckFile : BaseEntity
{
    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public Guid UploadedBy { get; set; }
    public virtual ApplicationUser Uploader { get; set; } = null!;

    public string FileName { get; set; } = null!;
    public int TotalPlates { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CheckPlate> CheckPlates { get; set; } = new List<CheckPlate>();
}
