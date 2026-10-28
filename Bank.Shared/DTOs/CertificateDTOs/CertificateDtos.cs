using Bank.Shared.Enums;

namespace Bank.Shared.DTOs.CertificateDTOs
{
    // ============================ القائمة والبحث ============================

    /// <summary>
    /// SearchTerm/PageNumber/PageSize موروثين من PaginationParams.
    /// البحث: اسم الملف أو رقم اللوحة (حروف + أرقام مع بعض، مثل: ابم7263).
    /// </summary>
    public class CertificateQueryDto : PaginationParams
    {
        /// <summary>latest-batch (أحدث دفعة) | today (اليوم) | all (الافتراضي). بيتجاهل لو BatchNo اتحدد.</summary>
        public string? Scope { get; set; }
        public int? BatchNo { get; set; }
        public string? Status { get; set; }           // Pending | Processing | Extracted | NeedsReview | Failed
        public int? TzOffsetMinutes { get; set; }     // لحساب "اليوم" بتوقيت المستخدم (الافتراضي 180)
    }

    public record CertificateListItemDto(
        Guid Id,
        string FileName,
        string? PlateDisplay,          // ر ع ط 4428
        string? NormalizedPlate,       // رعط4428
        string? Make,
        string? Model,
        string? Color,
        string? Lessor,
        string Status,
        bool NeedsReview,
        int BatchNo,
        DateTime AddedAt,
        DateTime DriveCreatedAt);

    public record CertificateDetailDto(
        Guid Id,
        string FileName,
        string Provider,
        string? PlateLetters,
        string? PlateDigits,
        string? PlateDisplay,
        string? NormalizedPlate,
        string? Make,
        string? Model,
        string? Color,
        string? Lessor,
        string Status,
        string? StatusNote,
        bool IsManuallyEdited,
        bool HasLocalCopy,
        int BatchNo,
        DateTime AddedAt,
        DateTime DriveCreatedAt);

    // ============================ الدفعات ============================

    /// <summary>الدفعة = الشهادات اللي اتكتشفت مع بعض في نفس دورة مزامنة. كل الأرقام بتتحسب وقت الطلب.</summary>
    public record CertificateBatchDto(
        int BatchNo,
        DateTime CreatedAt,
        int Total,
        int Extracted,
        int NeedsReview,
        int Failed,
        int Pending,
        bool IsReady);                 // مفيش شهادة لسه Pending/Processing

    // ============================ الفرز ============================

    public class CertificateSortRequestDto
    {
        /// <summary>لو null بيتاخد أحدث دفعة.</summary>
        public int? BatchNo { get; set; }
        public FilterType FilterType { get; set; } = FilterType.New;
    }

    /// <summary>نتيجة إنشاء الفرز: ReferralJobId بيتكمّل بيه من api/referral-jobs/{id}/... زي أي ملف إحالة.</summary>
    public record CertificateSortResultDto(
        Guid ReferralJobId,
        int BatchNo,
        int PlatesSent,
        int DuplicatePlates,
        int SkippedWithoutPlate,
        int SkippedFailed);

    // ============================ الأدمن ============================

    public class UpdateCertificateDto
    {
        public string? PlateLetters { get; set; }   // 1-3 حروف عربية بالترتيب المنطقي
        public string? PlateDigits { get; set; }    // 1-4 أرقام
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Color { get; set; }
        public string? Lessor { get; set; }
    }

    public record RetryResultDto(int Requeued);

    public record CertificateSystemStatusDto(
        bool DriveConfigured,
        string DriveState,              // NotConfigured | Unknown | Ok | NeedsReauth
        string? DriveAccountEmail,
        DateTime? LastSyncAt,
        int LastSyncNewFiles,
        string? LastError,
        int LatestBatchNo,
        int Pending,
        int Processing,
        int Extracted,
        int NeedsReview,
        int Failed);
}
