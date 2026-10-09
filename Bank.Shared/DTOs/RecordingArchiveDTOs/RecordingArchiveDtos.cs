using Bank.Shared.DTOs;

namespace Bank.Shared.DTOs.RecordingArchiveDTOs;

public record RecordingArchiveRowDto(
    long Id,
    string NormalizedPlate,
    string RawPlate,
    string? Notes,
    string? VehicleType,
    string? District,
    string? Street,
    DateTime RecordedAt,
    string? LocationLink,
    string? RecorderName,
    Guid? SourceSessionId,
    Guid? AppendedToDataFileId,
    bool IsMatched);
public class UpdateRecordingArchiveRowDto
{
    public string RawPlate { get; set; } = null!;
    public string? VehicleType { get; set; }
    public string? District { get; set; }
    public string? Street { get; set; }
    public string? Notes { get; set; }
    public string? RecorderName { get; set; }
    public bool ApplyDistrictStreetToWholeSession { get; set; } = false;
}
// Title بيتحسب بتاريخ آخر صف اتضاف في الجدول (أو تاريخ النهاردة لو الجدول فاضي) —
// ده للعرض في الـ UI. التصدير بيستخدم تاريخ التنزيل نفسه بدل ده.
public record RecordingArchiveListResponseDto(string Title, PagedResult<RecordingArchiveRowDto> Items);

public class ConvertArchiveToDataFileRequestDto
{
    // حدد واحد بس من الاتنين دول.
    public Guid? ExistingDataFileId { get; set; }
    public string? NewDataFileName { get; set; }
}

public record ConvertArchiveToDataFileResponseDto(Guid DataFileId, int AppendedCount);

// تعديل حي/شارع الـ RecordingSession قبل "تفريغ تاني" — ممنوع وقت ما السيشن
// لسه بتتعالج (Status == Processing).
public class UpdateRecordingSessionRequestDto
{
    public string? District { get; set; }
    public string? Street { get; set; }
}
