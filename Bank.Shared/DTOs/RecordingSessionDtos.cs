using Bank.Shared.Enums;

namespace Bank.Shared.DTOs.RecordingSessionDTOs;
//public class ManualCheckRequestDto
//{
//    public string PlateNumber { get; set; } = null!;
//    public string? VehicleType { get; set; }
//}
//public class ManualCheckResponseDto
//{
//    public string PlateNumber { get; set; }
//    public bool IsMatched { get; set; }
//    public string? VehicleType { get; set; }
//}
public class CreateManualCheckRequestDto
{
    public string District { get; set; } = null!;
    public string Street { get; set; } = null!;

    public string? ExcelSheetName { get; set; }

    public string PlateNumber { get; set; } = null!;

    public string? VehicleType { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public record ManualCheckResultDto(Guid SessionId, string DisplayName, RecordedPlateRowDto Plate);

public class CreateRecordingSessionRequestDto
{
    public string District { get; set; } = null!;
    public string Street { get; set; } = null!;
    public string ExcelSheetName { get; set; } = null!;
    public TrackingMode TrackingMode { get; set; } = TrackingMode.Auto;
    public int TrackingIntervalSeconds { get; set; } = 3;
}

public record CreateRecordingSessionResponseDto(Guid Id, string DisplayName, SessionStatus Status);

public record GpsPointDto(double Latitude, double Longitude, DateTime CapturedAt, double? AccuracyMeters, bool IsManual);

public class AddGpsPointsRequestDto
{
    public List<GpsPointDto> Points { get; set; } = new();
}

public record RecordingSessionSummaryDto(
    Guid Id,
    string DisplayName,
    SessionStatus Status,
    int? DurationSeconds,
    int TotalGpsPoints,
    int TotalPlates,
    int MatchedCount,
    int UnmatchedCount,
    DateTime StartedAt);

public record RecordingSessionDetailDto(
    Guid Id,
    string DisplayName,
    string District,
    string Street,
    string ExcelSheetName,
    TrackingMode TrackingMode,
    int TrackingIntervalSeconds,
    SessionStatus Status,
    string? ProcessingError,
    int TotalGpsPoints,
    int TotalPlates,
    DateTime StartedAt,
    DateTime? EndedAt);

// ⭐ الجدول بالظبط زي الصورة: رقم اللوحة / ملاحظات / نوع السيارة / الشارع / الحي / التاريخ + isMatched + رابط الموقع
public record RecordedPlateRowDto(
    long Id,
    string NormalizedPlate,
    string? Notes,
    string? VehicleType,
    string? Street,
    string? District,
    DateTime RecordedAt,
    string? LocationLink,   // ⭐ رابط، مش lat/lng
    bool IsMatched);

public class AppendToDataFileRequestDto
{
    public Guid DataFileId { get; set; }
    public Bank.Shared.Enums.RecordingAppendMode AppendMode { get; set; } = Bank.Shared.Enums.RecordingAppendMode.All;
}

public record AppendToDataFileResultDto(int AppendedCount);
