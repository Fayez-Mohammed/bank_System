using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CertificateDTOs;

namespace Bank.Services.Interfaces.ICertificate;

/// <summary>ملف PDF جاهز يتبعت للمستخدم (stream مباشر من Drive أو من نسخة محلية قديمة). Length = null لو مش معروف.</summary>
public sealed record CertificateFile(Stream Stream, string FileName, long? Length);

public interface ICertificateService
{
    // ---------- لكل المستخدمين (سواء في مجموعة أو لا) ----------
    Task<ApiResponse<PagedResult<CertificateListItemDto>>> ListAsync(CertificateQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<CertificateDetailDto>> GetAsync(Guid certificateId, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<CertificateBatchDto>>> ListBatchesAsync(PaginationParams pagination, CancellationToken ct = default);
    Task<ApiResponse<CertificateBatchDto?>> GetLatestBatchAsync(CancellationToken ct = default);

    /// <summary>بيرمي KeyNotFoundException لو الشهادة أو الملف مش موجود. لازم الـ Stream يتعمله Dispose.</summary>
    Task<CertificateFile> OpenFileAsync(Guid certificateId, CancellationToken ct = default);

    // ---------- الفرز (بيتطلب صلاحية CanSort) ----------
    Task<ApiResponse<CertificateSortResultDto>> CreateSortAsync(
        Guid currentUserId, CertificateSortRequestDto request, CancellationToken ct = default);

    // ---------- للأدمن ----------
    Task<ApiResponse<CertificateSystemStatusDto>> GetSystemStatusAsync(bool liveCheck, CancellationToken ct = default);
    /// <summary>تفاصيل المزامنة والطابور (مراحل الفهرسة، آخر تشغيل، عدّادات Drive، workers).</summary>
    Task<ApiResponse<CertificateSyncStatusDto>> GetSyncStatusAsync(CancellationToken ct = default);
    Task<ApiResponse<bool>> TriggerSyncAsync(CancellationToken ct = default);
    /// <summary>يعيد الـ listing المسطّح كله (مثلاً لو اتضافت صلاحيات على مجلدات قديمة). مفيش تكرار ولا استخراج للقديم.</summary>
    Task<ApiResponse<bool>> RebuildIndexAsync(CancellationToken ct = default);
    /// <summary>فحص حي (قراءة بس) لـ Drive: Changes API، المجلدات المشتركة، آخر الملفات.</summary>
    Task<ApiResponse<CertificateDriveCheckDto>> CheckDriveAsync(CancellationToken ct = default);
    Task<ApiResponse<RetryResultDto>> RetryAsync(Guid certificateId, CancellationToken ct = default);
    /// <summary>includeStuck: يضيف كمان Processing العالقين أكتر من الـ lease، والـ Pending المتأجلة.</summary>
    Task<ApiResponse<RetryResultDto>> RetryFailedAsync(bool includeStuck = false, CancellationToken ct = default);
    Task<ApiResponse<CertificateDetailDto>> UpdateAsync(Guid certificateId, UpdateCertificateDto dto, CancellationToken ct = default);
}
