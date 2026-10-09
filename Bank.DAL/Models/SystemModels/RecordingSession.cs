using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

public class RecordingSession : BaseEntity
{
    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public bool IsRecord { get; set; } = true; 
    public Guid? CheckFileId { get; set; }
    public virtual CheckFile? CheckFile { get; set; }

    public string District { get; set; } = null!;         // الحي: الراشديه
    public string Street { get; set; } = null!;            // الشارع: 110 البيعه
    public string ExcelSheetName { get; set; } = null!;     // اسم ورقة Excel

    public string DisplayName { get; set; } = null!;
    public string? RecorderName { get; set; } 
    public TrackingMode TrackingMode { get; set; } = TrackingMode.Auto;
    public int TrackingIntervalSeconds { get; set; } = 3;   // الفترة بين النقاط

    public int TotalGpsPoints { get; set; }
    public int TotalPlates { get; set; }
    public string? AudioUrl { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Pending;

    // ⭐ جديد — نفس فكرة DataFile.ImportError
    public string? ProcessingError { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    //public DateTime? MatchesShownAt { get; set; }
    public virtual ICollection<RecordedPlate> RecordedPlates { get; set; } = new List<RecordedPlate>();

    // ⭐ جديد
    public virtual ICollection<RecordingGpsPoint> GpsPoints { get; set; } = new List<RecordingGpsPoint>();
}
