//using Bank.Shared.Enums;
//using Microsoft.AspNetCore.Http;

//namespace Bank.Shared.DTOs.ReferralJobDTOs;

//public record SheetInfoDto(int Index, string Name, int RowCount);

//public record InspectReferralFileResponseDto(List<SheetInfoDto> Sheets);


//public class CreateReferralJobUploadRequestDto
//{
//    public IFormFile File { get; set; } = null!;
//    public Bank.Shared.Enums.FilterType FilterType { get; set; }
//    public string? Password { get; set; }

//    public bool AllSheets { get; set; } = true;
//    public List<int>? SelectedSheetIndexes { get; set; }
//}
//public record ReferralJobSummaryDto(
//    Guid Id,
//    Bank.Shared.Enums.FilterType FilterType,
//    Bank.Shared.Enums.SourceType SourceType,
//    Bank.Shared.Enums.JobStatus Status,
//    int TotalReferralPlates,
//    int SkippedPlatesCount,
//    int MatchedPlatesCount,
//    int UnmatchedPlatesCount,
//    DateTime CreatedAt);
////public class CreateReferralJobUploadRequestDto
////{
////    public IFormFile File { get; set; } = null!;
////    public FilterType FilterType { get; set; }
////    public string? Password { get; set; }
////}

//public class CreateReferralJobPasteRequestDto
//{
//    public FilterType FilterType { get; set; }

//    public string PlatesText { get; set; } = null!;
//}

//public record ReferralJobStatusDto(
//    Guid Id,
//    JobStatus Status,
//    FilterType FilterType,
//    SourceType SourceType,
//    int TotalReferralPlates,
//    int SkippedPlatesCount,
//    int MatchedPlatesCount,
//    int UnmatchedPlatesCount,   
//    DateTime CreatedAt);


//public record MatchResultRowDto(
//    string NormalizedPlate,
//    string? FoundVehicleType,
//    string? FoundDistrict,
//    string? FoundStreet,
//    string? Location,
//    string? FoundRecordedAt,
//    string SourceDataFileName);

//public record UnmatchedPlateRowDto(string RawPlate, string NormalizedPlate);

//public record PagedResultDto<T>(List<T> Items, int TotalCount, int Page, int PageSize);
using Bank.Shared.Enums;
using Microsoft.AspNetCore.Http;

namespace Bank.Shared.DTOs.ReferralJobDTOs;

// ========== Inspect (قبل الرفع الفعلي) ==========

// اقتراحات السيرفر لكل شيت — المستخدم بيأكدها أو يغيّرها، مش نهائية.
public class ExportCombinedReferralResultsRequestDto
{
    public List<Guid> ReferralJobIds { get; set; } = new();
}
public record SuggestedReferralColumnsDto(
    int? PlateColumnIndex,
    string? ColorColumnHeader,
    string? VehicleTypeColumnHeader,
    string? ModelColumnHeader);

public record SheetInfoDto(
    int Index,
    string Name,
    int RowCount,
    List<string?> Headers,
    SuggestedReferralColumnsDto Suggestions,
    // index الصف اللي اتحدد كهيدر حقيقي (مش بالضرورة صفر — ممكن يبقى فيه صفوف
    // عنوان/فاضية أو هيدر مترجم زيادة فوقه). المستخدم يقدر يغيّره لو غلط.
    int HeaderRowIndex);

public record InspectReferralFileResponseDto(List<SheetInfoDto> Sheets);

// ========== إنشاء عملية فرز من ملف مرفوع ==========

public class SheetSelectionDto
{
    public int SheetIndex { get; set; }
    public int PlateColumnIndex { get; set; }

    // نفس الـ HeaderRowIndex اللي رجع من upload/inspect (أو المستخدم عدّله).
    // لو اتسابت 0 والملف فعلاً مالوش صفوف عنوان فوق الهيدر، فده هو السلوك الصح برضه.
    public int HeaderRowIndex { get; set; }
}

public class CreateReferralJobUploadRequestDto
{
    public IFormFile File { get; set; } = null!;
    public FilterType FilterType { get; set; }
    public string? Password { get; set; }

    // الشيتات اللي هيتم قراءتها، وعمود اللوحة النهائي وصف الهيدر النهائي (بعد تأكيد المستخدم) لكل واحدة.
    public List<SheetSelectionDto> Sheets { get; set; } = new();

    // أسماء أعمدة الإحالة (من ملف الإحالة نفسه) اللي عايزين تظهر في النتيجة، بترتيبها.
    // القيم بتتاخد بمطابقة اسم العمود في كل شيت على حدة.
    public List<string> SelectedReferralColumns { get; set; } = new();

    // أعمدة الداتا التاريخية اللي عايزين تظهر. null/فاضية = كل الأعمدة الثمانية.
    public List<HistoricalDataColumn>? SelectedHistoricalColumns { get; set; }
}

// ========== إنشاء عملية فرز من نص ملصوق ==========

public class CreateReferralJobPasteRequestDto
{
    public FilterType FilterType { get; set; }
    public string PlatesText { get; set; } = null!;
    public List<HistoricalDataColumn>? SelectedHistoricalColumns { get; set; }
}

// ========== ملخص وحالة العملية ==========

public record ReferralJobSummaryDto(
    Guid Id,
    FilterType FilterType,
    SourceType SourceType,
    JobStatus Status,
    int TotalReferralPlates,
    int SkippedPlatesCount,
    int MatchedPlatesCount,
    int UnmatchedPlatesCount,
    DateTime CreatedAt);

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

// ========== نتيجة الفرز (مجمّعة بالملف، ملوّنة، وبالأعمدة المختارة) ==========

public record ReferralResultRowDto(
    long MatchResultId,
    long HistoricalPlateId,
    string NormalizedPlate,
    string RawPlate,
    int? ColorIndex,                              // null = لوحة فريدة (من غير تلوين)
    Dictionary<string, string?> DataColumns,       // key = اسم الـ HistoricalDataColumn المختار
    Dictionary<string, string?> ReferralColumns);  // key = اسم عمود الإحالة المختار

public record ReferralResultGroupDto(
    Guid DataFileId,
    string DataFileName,
    int SortOrder,
    List<ReferralResultRowDto> Rows);

public record ReferralResultsResponseDto(
    List<string> SelectedHistoricalColumns,
    List<string> SelectedReferralColumns,
    List<ReferralResultGroupDto> Groups);

public record UnmatchedPlateRowDto(long ReferralPlateId, string RawPlate, string NormalizedPlate);

public record PagedResultDto<T>(List<T> Items, int TotalCount, int Page, int PageSize);

// ========== حذف لوحة من نتيجة الفرز أو من الداتا نهائيًا ==========

public record DeleteMatchedPlateResponseDto(bool DeletedFromDataFile);

// ========== إلحاق الغير مفرزة وواتساب ==========

public class AppendUnmatchedToCheckFileRequestDto
{
    public string? BankName { get; set; }
}

public record AppendUnmatchedResultDto(int AppendedCount, int SkippedAlreadyInCheckFileCount);
public record WhatsAppShareLinkDto(string ShareUrl, string MessagePreview);
