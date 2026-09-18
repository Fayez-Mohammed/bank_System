using Bank.Shared.DTOs;
using Bank.Shared.DTOs.RecordingSessionDTOs;
using Microsoft.AspNetCore.Http;

namespace Bank.Services.Interfaces;

public interface IRecordingSessionService
{
    Task<ApiResponse<CreateRecordingSessionResponseDto>> CreateAsync(
        Guid userId, CreateRecordingSessionRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> AddGpsPointsAsync(
        Guid sessionId, Guid currentUserId, AddGpsPointsRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> CompleteAsync(
        Guid sessionId, Guid currentUserId, IFormFile audioFile, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> RetryAsync(Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<List<RecordingSessionSummaryDto>>> GetAllAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<RecordingSessionDetailDto>> GetByIdAsync(Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResult<RecordedPlateRowDto>>> GetPlatesAsync(
        Guid sessionId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<(Stream Stream, string ContentType, string FileName)> GetAudioAsync(
        Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<(byte[] FileBytes, string FileName)> ExportAsync(
        Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<AppendToDataFileResultDto>> AppendToDataFileAsync(
        Guid sessionId, Guid currentUserId, AppendToDataFileRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteAsync(Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<int>> DeleteAllAsync(Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ApiResponse<ManualCheckResultDto>> CreateManualCheckAsync(
    Guid userId, CreateManualCheckRequestDto request, CancellationToken cancellationToken = default);
    // Task<ApiResponse<ManualCheckResponseDto>> ManualCheckPlateAsync(
    //   Guid currentUserId, ManualCheckRequestDto request, CancellationToken cancellationToken = default);
}
