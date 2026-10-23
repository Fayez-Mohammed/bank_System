using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

// ⭐ محدّثة — أضفنا CheckSessionId و VehicleType (الـ AI بقى بيرجّعها)،
// و Location بقى string (رابط) متسق مع باقي القرارات الأخيرة، مش geography.
public class LiveCheckLog : BaseEntityLong
{
    public Guid CheckSessionId { get; set; }
    public virtual CheckSession CheckSession { get; set; } = null!;

    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;
    public CheckType CheckType { get; set; } = CheckType.Live;
    public string? VehicleType { get; set; }

    public string? District { get; set; }
    public string? Street { get; set; }
    public string? Location { get; set; }   // رابط Google Maps، مش lat/lng خام

    public bool IsMatched { get; set; }
    public long? MatchedCheckPlateId { get; set; }
    public virtual CheckPlate? MatchedCheckPlate { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    // لو الصف ده اتحوّل لملف داتا قبل كده، عشان المرة الجاية نلحق الجديد بس
    // (زي RecordingArchivePlate بالظبط — التحويل تراكمي ومش بيمسح التشييك).
    public Guid? AppendedToDataFileId { get; set; }
    public DateTime? AppendedAt { get; set; }
}
