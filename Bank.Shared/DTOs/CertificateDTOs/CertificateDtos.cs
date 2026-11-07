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
        public string? Origin { get; set; }           // New | Historical (الافتراضي: الاتنين)
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
        DateTime DriveCreatedAt,
        string Origin = "New",                 // New | Historical (Historical = Status Indexed: بحث/عرض/تنزيل بس)
        string? ParentFolderName = null);

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
        DateTime DriveCreatedAt,
        string Origin = "New",
        string? ParentFolderId = null,
        string? ParentFolderName = null,
        long? SizeBytes = null,
        DateTime? DriveModifiedAt = null,
        int AttemptCount = 0);

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
        int Failed,
        // ---- إضافات (كلها اختيارية عشان العقد القديم يفضل شغال) ----
        int Indexed = 0,                       // ملفات تاريخية متفهرسة (من غير استخراج)
        int DeletedInDrive = 0,
        string BaselineStatus = "NotStarted",  // NotStarted | Running | Completed
        int BaselineFilesSeen = 0,
        DateTime? BaselineStartedAt = null,
        DateTime? BaselineCompletedAt = null,
        string? LastSyncKind = null,           // Baseline | Incremental | Reconcile
        int LastSyncFilesListed = 0,
        int LastSyncAlreadyIndexed = 0,
        int LastSyncIndexedHistorical = 0,
        int LastSyncDriveRequests = 0,
        int LastSyncDurationMs = 0,
        long TotalDriveRequests = 0,
        int ExtractedLastHour = 0,
        int? AvgExtractionMsLastHour = null,
        DateTime? ExtractionPausedUntil = null);   // الـ worker واقف مؤقتاً (Drive auth / quota)

    /// <summary>نتيجة فحص تشخيصي لإعداد Drive (بيأكد إن القايمة المسطّحة والـ Changes API شغالين على الحساب ده).</summary>
    public record DriveProbeDto(
        bool Configured,
        string? AccountEmail,
        bool ChangesTokenOk,
        int FlatPdfFirstPageCount,
        bool FlatPdfHasMorePages,
        int SharedWithMeTopLevelCount,
        List<string> SampleFileNames,
        string? Error);
}
