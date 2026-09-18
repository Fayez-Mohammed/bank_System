using Bank.Shared.Enums;
using Microsoft.AspNetCore.Http;

namespace Bank.Shared.DTOs.ReferralJobDTOs;

public record SheetInfoDto(int Index, string Name, int RowCount);

public record InspectReferralFileResponseDto(List<SheetInfoDto> Sheets);


public class CreateReferralJobUploadRequestDto
{
    public IFormFile File { get; set; } = null!;
    public Bank.Shared.Enums.FilterType FilterType { get; set; }
    public string? Password { get; set; }

    public bool AllSheets { get; set; } = true;
    public List<int>? SelectedSheetIndexes { get; set; }
}
public record ReferralJobSummaryDto(
    Guid Id,
    Bank.Shared.Enums.FilterType FilterType,
    Bank.Shared.Enums.SourceType SourceType,
    Bank.Shared.Enums.JobStatus Status,
    int TotalReferralPlates,
    int SkippedPlatesCount,
    int MatchedPlatesCount,
    int UnmatchedPlatesCount,
    DateTime CreatedAt);
//public class CreateReferralJobUploadRequestDto
//{
//    public IFormFile File { get; set; } = null!;
//    public FilterType FilterType { get; set; }
//    public string? Password { get; set; }
//}

public class CreateReferralJobPasteRequestDto
{
    public FilterType FilterType { get; set; }

    public string PlatesText { get; set; } = null!;
}

public record ReferralJobStatusDto(
    Guid Id,
    JobStatus Status,
    FilterType FilterType,
    SourceType SourceType,
    int TotalReferralPlates,
    int SkippedPlatesCount,
    int MatchedPlatesCount,
    int UnmatchedPlatesCount,   
    DateTime CreatedAt);


public record MatchResultRowDto(
    string NormalizedPlate,
    string? FoundVehicleType,
    string? FoundDistrict,
    string? FoundStreet,
    string? Location,
    string? FoundRecordedAt,
    string SourceDataFileName);

public record UnmatchedPlateRowDto(string RawPlate, string NormalizedPlate);

public record PagedResultDto<T>(List<T> Items, int TotalCount, int Page, int PageSize);
