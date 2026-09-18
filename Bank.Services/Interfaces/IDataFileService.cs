using Bank.Shared.DTOs;
using Bank.Shared.DTOs.DataFileDTOs;

namespace Bank.Services.Interfaces;

public interface IDataFileService
{
    Task<ApiResponse<DataFilesListResponseDto>> GetDataFilesAsync(
        Guid currentUserId, CancellationToken cancellationToken = default);


    Task<ApiResponse<Guid>> CreateDataFileAsync(
        Guid uploaderId, CreateDataFileRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ToggleActiveAsync(
        Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ReorderAsync(
        Guid currentUserId, ReorderDataFilesRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteAsync(
        Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<(Stream Stream, string ContentType, string FileName)> DownloadAsync(
        Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> AppendAsync(
        Guid dataFileId, Guid currentUserId, AppendDataFileRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<DataFileImportStatusDto>> GetImportStatusAsync(
    Guid dataFileId, Guid currentUserId, CancellationToken cancellationToken = default);

}