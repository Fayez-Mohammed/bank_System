using Bank.Shared.Enums;

namespace Bank.Shared.DTOs.LiveCheckDTOs;

public record LiveCheckPlateResultDto(
    long Id, string NormalizedPlate, string? VehicleType, bool IsMatched,
    string? LocationLink, DateTime CheckedAt);

public class CreateManualLiveCheckRequestDto
{
    public string RecorderName { get; init; } = null!;
    public string District { get; set; } = null!;
    public string Street { get; set; } = null!;
    public string? ExcelSheetName { get; set; }
    public string PlateNumber { get; set; } = null!;
    public string? VehicleType { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public record ManualLiveCheckResultDto(Guid SessionId, string DisplayName, LiveCheckPlateResultDto Plate);

public record CheckSessionSummaryDto(
    Guid Id, string DisplayName, string RecorderName, SessionStatus Status, int TotalPlates,
    int MatchedCount, int UnmatchedCount, DateTime StartedAt, DateTime? EndedAt);

public record LiveCheckLogRowDto(
    long Id, string NormalizedPlate, string? LocationLink, DateTime CheckedAt,
    string? District, string? Street, string RecorderName, string? VehicleType, bool IsMatched);

// نفس فكرة ConvertArchiveToDataFileRequestDto بتاعة التسجيل — حدد واحد بس من الاتنين.
public class ConvertCheckLogsToDataFileRequestDto
{
    public Guid? ExistingDataFileId { get; set; }
    public string? NewDataFileName { get; set; }
}

public record ConvertCheckLogsToDataFileResponseDto(Guid DataFileId, int AppendedCount);
