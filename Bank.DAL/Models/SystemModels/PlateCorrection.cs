using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

// Add-only history of what users changed on plates, read by the AI server for training.
// Deliberately has no foreign keys: rows must outlive deleted plates, sessions and re-processing.
public class PlateCorrection : BaseEntityLong
{
    public Guid? SessionId { get; set; }
    public PlateCorrectionSessionType SessionType { get; set; }

    public long? PlateId { get; set; }          // RecordedPlate or LiveCheckLog id
    public long? ArchiveRowId { get; set; }     // RecordingArchivePlate id (recordings only)
    public bool IsManualPlate { get; set; }     // the plate came from a user, not from the AI

    public PlateCorrectionAction Action { get; set; }

    public string? OldPlate { get; set; }
    public string? NewPlate { get; set; }
    public string? OldVehicleType { get; set; }
    public string? NewVehicleType { get; set; }
    public string? OldNotes { get; set; }
    public string? NewNotes { get; set; }

    public int? AudioOffsetMs { get; set; }
    public int? AudioStartMs { get; set; }
    public int? AudioEndMs { get; set; }

    public Guid UserId { get; set; }
    public string? RecorderName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
