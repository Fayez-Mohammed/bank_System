using Microsoft.AspNetCore.Http;

namespace Bank.Shared.DTOs.MapDTOs;

public record MapResultRowDto(
    string NormalizedPlate,
    string? VehicleType,
    string? District,
    string? Street,
    string? Notes,
    string? RecordedAt,
    string? RecorderName,
    string? LocationLink,
    double? Latitude,
    double? Longitude);

public record MapResultDto(
    Guid ReferralJobId,
    int CarCount,
    int UnresolvedCount,
    DateTime PreparedAt,
    List<MapResultRowDto> Rows);

public record MapUploadedFileRowDto(
    long Id,
    string NormalizedPlate,
    string? VehicleType,
    string? District,
    string? Street,
    string? Notes,
    string? RecordedAt,
    string? RecorderName,
    string? LocationLink,
    double? Latitude,
    double? Longitude);

public record MapUploadedFileDto(
    Guid Id,
    string FileName,
    int RowCount,
    int CarCount,
    int UnresolvedCount,
    DateTime UploadedAt,
    List<MapUploadedFileRowDto> Rows);

public class UploadMapFileRequestDto
{
    public IFormFile File { get; set; } = null!;

    // اختياري — لو الملف فيه أكتر من Sheet. الافتراضي أول Sheet (index 0).
    public int? SheetIndex { get; set; }
}
