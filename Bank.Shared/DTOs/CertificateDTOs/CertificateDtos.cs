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
        public string? Status { get; set; }           // Pending | Processing | Extracted | NeedsReview | Failed | Indexed
        public int? TzOffsetMinutes { get; set; }     // لحساب "اليوم" بتوقيت المستخدم (الافتراضي 180)

        /// <summary>
        /// الملفات القديمة (Indexed) بتظهر تلقائي مع البحث بالاسم. true = تظهر كمان من غير بحث.
        /// </summary>
        public bool? IncludeHistorical { get; set; }
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
        bool IsRemovedFromDrive = false);   // الملف اتمسح/اتنقل من Drive (البيانات المستخرجة لسه موجودة)

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
        bool IsRemovedFromDrive = false,
        string? ParentFolderId = null,
        DateTime? DriveModifiedAt = null,
        long? FileSize = null,
        int ExtractionAttempts = 0,
        DateTime? ExtractedAt = null);

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
        int Indexed = 0,                       // ملفات قديمة متفهرسة (من غير استخراج)
        int RemovedFromDrive = 0,
        string BaselinePhase = "NotStarted",   // NotStarted | Nodes | Files | Completed
        DateTime? BaselineCompletedAt = null);

    // ============================ متابعة المزامنة ============================

    public record CertificateDriveApiStatsDto(
        long Requests, long Retries, long Throttled, long Failures, DateTime? LastThrottledAt, string? LastFailure);

    public record CertificateSyncStatusDto(
        string BaselinePhase,
        DateTime? BaselineStartedAt,
        DateTime? BaselineCompletedAt,
        DateTime? ExtractCutoff,                // قبله = قديم (Indexed)، بعده = جديد (استخراج)
        long BaselineNodesListed,
        long BaselineFilesListed,
        long BaselineFilesInScope,
        bool ChangesTokenSaved,
        DateTime? LastChangesSyncAt,
        DateTime? ReconcileWatermark,
        DateTime? LastRunStartedAt,
        DateTime? LastRunFinishedAt,
        bool RunInProgress,
        System.Text.Json.JsonElement? LastRunStats,   // عدّادات آخر تشغيل (بتتحدث بعد كل صفحة)
        string? LastError,
        DateTime? LastErrorAt,
        int ConsecutiveFailures,
        int DriveNodes,
        int DriveNodesInScope,
        int SharedRoots,
        int FoldersAwaitingScan,
        Dictionary<string, int> CertificatesByStatus,
        int PendingWaitingRetry,
        DateTime? OldestPendingDiscoveredAt,
        int ActiveExtractionWorkers,
        int ExtractionWorkerLimit,
        CertificateDriveApiStatsDto DriveApi);

    public record CertificateDriveCheckFileDto(
        string DriveFileId, string Name, DateTime? CreatedAt, string? ParentId, string? IndexedStatus);

    public record CertificateDriveCheckDto(
        string? AccountEmail,
        bool ChangesApiAvailable,
        int SharedFolderRoots,
        bool MoreSharedFolderRoots,
        List<CertificateDriveCheckFileDto> LatestPdfs,
        int? PendingPdfChangesPeek,             // PDFs في الـ Changes feed من الـ token المحفوظ (من غير ما يتحرك)
        List<string> Errors);
}
