using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CertificateDTOs;

namespace Bank.Services.Interfaces.ICertificate;

public interface ICertificateService
{
    // ---------- لكل المستخدمين (سواء في مجموعة أو لا) ----------
    Task<ApiResponse<PagedResult<CertificateListItemDto>>> ListAsync(CertificateQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<CertificateDetailDto>> GetAsync(Guid certificateId, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<CertificateBatchDto>>> ListBatchesAsync(PaginationParams pagination, CancellationToken ct = default);
    Task<ApiResponse<CertificateBatchDto?>> GetLatestBatchAsync(CancellationToken ct = default);

    /// <summary>بيرمي KeyNotFoundException لو الشهادة أو الملف مش موجود.</summary>
    Task<(Stream Stream, string FileName)> OpenFileAsync(Guid certificateId, CancellationToken ct = default);

    // ---------- الفرز (بيتطلب صلاحية CanSort) ----------
    Task<ApiResponse<CertificateSortResultDto>> CreateSortAsync(
        Guid currentUserId, CertificateSortRequestDto request, CancellationToken ct = default);

    // ---------- للأدمن ----------
    Task<ApiResponse<CertificateSystemStatusDto>> GetSystemStatusAsync(bool liveCheck, CancellationToken ct = default);
    Task<ApiResponse<bool>> TriggerSyncAsync(CancellationToken ct = default);
    Task<ApiResponse<RetryResultDto>> RetryAsync(Guid certificateId, CancellationToken ct = default);
    /// <summary>includeStuck: يضيف كمان Pending/Processing العالقين من أكتر من ساعتين.</summary>
    Task<ApiResponse<RetryResultDto>> RetryFailedAsync(bool includeStuck = false, CancellationToken ct = default);
    Task<ApiResponse<CertificateDetailDto>> UpdateAsync(Guid certificateId, UpdateCertificateDto dto, CancellationToken ct = default);
}
