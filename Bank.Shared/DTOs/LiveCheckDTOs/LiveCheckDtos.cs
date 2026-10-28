using Bank.Shared.Enums;

namespace Bank.Shared.DTOs.LiveCheckDTOs;

public record LiveCheckPlateResultDto(
    long Id, string NormalizedPlate, string? VehicleType, bool IsMatched,
    string? LocationLink, DateTime CheckedAt, string? BankName,
    string? VehicleTypeInCheckFile);
public class UpdateLiveCheckLogRowDto
{
    public string RawPlate { get; set; } = null!;
    public string? VehicleType { get; set; }
    public string? District { get; set; }
    public string? Street { get; set; }

    // اختياري: لو اتبعت، بيغيّر اسم المسجل على الجلسة كلها (لأنه محفوظ على الجلسة مش على الصف)
    public string? RecorderName { get; set; }

    // لو true: الحي والشارع يتطبّقوا على الجلسة وكل صفوفها
    public bool ApplyDistrictStreetToWholeSession { get; set; } = false;
}
public class CreateManualLiveCheckRequestDto
{
    // اختياريين في التشييك.
    public string? RecorderName { get; init; }
    public string? District { get; set; }
    public string? Street { get; set; }
    public string? ExcelSheetName { get; set; }
    public string PlateNumber { get; set; } = null!;
    public string? VehicleType { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public record ManualLiveCheckResultDto(Guid SessionId, string DisplayName, LiveCheckPlateResultDto Plate);

public record CheckSessionSummaryDto(
    Guid Id, string DisplayName, string? RecorderName, SessionStatus Status, int TotalPlates,
    int MatchedCount, int UnmatchedCount, DateTime StartedAt, DateTime? EndedAt);

public record LiveCheckLogRowDto(
    long Id, string NormalizedPlate, string? LocationLink, DateTime CheckedAt,
    string? District, string? Street, string? RecorderName, string? VehicleType, bool IsMatched);

// نفس فكرة ConvertArchiveToDataFileRequestDto بتاعة التسجيل — حدد واحد بس من الاتنين.
public class ConvertCheckLogsToDataFileRequestDto
{
    public Guid? ExistingDataFileId { get; set; }
    public string? NewDataFileName { get; set; }
}

public record ConvertCheckLogsToDataFileResponseDto(Guid DataFileId, int AppendedCount);
