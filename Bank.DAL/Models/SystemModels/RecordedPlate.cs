using Bank.DAL.Models.BaseModels;
using NetTopologySuite.Geometries;

namespace Bank.DAL.Models.SystemModels;

public class RecordedPlate : BaseEntityLong
{
    public Guid SessionId { get; set; }
    public RecordingSession Session { get; set; } = null!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;


    public int SequenceNumber { get; set; }

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;
    public string? VehicleType { get; set; }
    public string? Notes { get; set; }                      // نقل، باص، ونيد، إلخ

    public string? Street { get; set; }
    public string? District { get; set; }

   
    public Point? Location { get; set; }

    public int? AudioOffsetMs { get; set; }

    public bool IsMatched { get; set; }                     // 1 if found in active check file (Green Row)
    public long? MatchedCheckPlateId { get; set; }
    public CheckPlate? MatchedCheckPlate { get; set; }

    public bool IsManualEntry { get; set; }                 // يدوي أثناء التسجيل
    public string? BankName { get; set; }                          // اسم البنك
    public string? VehicleTypeInCheckFile { get; set; }     // نوع السيارة في ملف التحقق
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
