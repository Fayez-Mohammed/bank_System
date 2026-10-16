using Bank.Shared.DTOs;
using Bank.Shared.DTOs.ReferralJobDTOs;
using Microsoft.AspNetCore.Http;

namespace Bank.Services.Interfaces;

public interface IReferralJobService
{
    Task<ApiResponse<Guid>> CreateFromActiveCheckFileAsync(
    Guid userId, CancellationToken cancellationToken = default);
    Task<(byte[] FileBytes, string FileName)> ExportCombinedResultsAsync(
    List<Guid> referralJobIds, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ApiResponse<InspectReferralFileResponseDto>> InspectUploadAsync(
    IFormFile file, CancellationToken cancellationToken = default);
    Task<ApiResponse<Guid>> CreateFromUploadAsync(
        Guid userId, CreateReferralJobUploadRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<Guid>> CreateFromPasteAsync(
        Guid userId, CreateReferralJobPasteRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<ReferralJobStatusDto>> GetStatusAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);

    // نتيجة الفرز مجمّعة بالـ data file، مع تلوين اللوحات المتكررة، وبالأعمدة
    // (تاريخية وإحالة) اللي المستخدم اختارها على الـ job.
    Task<ApiResponse<ReferralResultsResponseDto>> GetResultsAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>> GetUnmatchedResultsAsync(
        Guid referralJobId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default);


    Task<ApiResponse<AppendUnmatchedResultDto>> AppendAllToCheckFileAsync(
        Guid referralJobId, Guid currentUserId, AppendUnmatchedToCheckFileRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<WhatsAppShareLinkDto>> GetWhatsAppShareLinkAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResultDto<ReferralJobSummaryDto>>> GetReferralJobsAsync(
    Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<DeleteMatchedPlateResponseDto>> DeleteMatchedPlateAsync(
        Guid referralJobId, long matchResultId, Guid currentUserId, bool deleteFromDataFile, CancellationToken cancellationToken = default);

    Task<(byte[] FileBytes, string FileName)> ExportMatchedAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<(byte[] FileBytes, string FileName)> ExportUnmatchedAsync(
        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<(byte[] FileBytes, string FileName)> ExportResultsAsync(
        Guid referralJobId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
//public interface IReferralJobService
//{
//    Task<ApiResponse<InspectReferralFileResponseDto>> InspectUploadAsync(
//    IFormFile file, CancellationToken cancellationToken = default);
//    Task<ApiResponse<Guid>> CreateFromUploadAsync(
//        Guid userId, CreateReferralJobUploadRequestDto request, CancellationToken cancellationToken = default);

//    Task<ApiResponse<Guid>> CreateFromPasteAsync(
//        Guid userId, CreateReferralJobPasteRequestDto request, CancellationToken cancellationToken = default);

//    Task<ApiResponse<ReferralJobStatusDto>> GetStatusAsync(
//        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);

//    Task<ApiResponse<PagedResultDto<MatchResultRowDto>>> GetMatchedResultsAsync(
//        Guid referralJobId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default);

//    Task<ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>> GetUnmatchedResultsAsync(
//        Guid referralJobId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default);


//    Task<ApiResponse<AppendUnmatchedResultDto>> AppendUnmatchedToCheckFileAsync(
//        Guid referralJobId, Guid currentUserId, AppendUnmatchedToCheckFileRequestDto request, CancellationToken cancellationToken = default);

//    Task<ApiResponse<WhatsAppShareLinkDto>> GetWhatsAppShareLinkAsync(
//        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);
//    Task<ApiResponse<PagedResultDto<ReferralJobSummaryDto>>> GetReferralJobsAsync(
//    Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default);

//    Task<ApiResponse<bool>> DeleteAsync(
//        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);


//    Task<(byte[] FileBytes, string FileName)> ExportMatchedAsync(
//        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);

//    Task<(byte[] FileBytes, string FileName)> ExportUnmatchedAsync(
//        Guid referralJobId, Guid currentUserId, CancellationToken cancellationToken = default);
//    Task<(byte[] FileBytes, string FileName)> ExportResultsAsync(
//        Guid referralJobId,
//        Guid currentUserId,
//        CancellationToken cancellationToken = default);
//}
