namespace Bank.Shared.DTOs.TrainingDTOs;

public record PlateCorrectionDto(
    long Id,
    Guid? SessionId,
    string SessionType,
    long? PlateId,
    long? ArchiveRowId,
    bool IsManualPlate,
    string Action,
    string? OldPlate,
    string? NewPlate,
    string? OldVehicleType,
    string? NewVehicleType,
    string? OldNotes,
    string? NewNotes,
    int? AudioOffsetMs,
    int? AudioStartMs,
    int? AudioEndMs,
    Guid UserId,
    string? RecorderName,
    DateTime CreatedAt);

// Pass LastId as afterId in the next call. HasMore = call again straight away.
public record PlateCorrectionFeedDto(List<PlateCorrectionDto> Items, long LastId, bool HasMore);

// A plate the user says was spoken but the AI missed, added inside an existing session.
public class AddSessionPlateRequestDto
{
    public string RawPlate { get; set; } = null!;
    public string? VehicleType { get; set; }
    public string? Notes { get; set; }
    public int? AudioOffsetMs { get; set; }
}

public record AddedSessionPlateDto(long PlateId, string NormalizedPlate, bool IsMatched);
