using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CheckFileDTos;

namespace Bank.Services.Interfaces;

public interface ICheckFileService
{
    Task<ApiResponse<List<CheckFileSummaryDto>>> GetCheckFilesAsync(
        Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<CheckFileSummaryDto>> UploadAsync(
        Guid uploaderId, CreateCheckFileRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ToggleActiveAsync(
        Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteAsync(
        Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReplaceBankPlatesResultDto>> ReplaceBankPlatesAsync(
    Guid currentUserId, ReplaceBankPlatesRequestDto request, CancellationToken cancellationToken = default);
    Task<(byte[] FileBytes, string FileName)> ExportAsync(
    Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default);
}
