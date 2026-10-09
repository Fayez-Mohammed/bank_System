using Bank.Shared.DTOs;
using Bank.Shared.DTOs.RecordingArchiveDTOs;

namespace Bank.Services.Interfaces;

public record RecordingArchiveEntryInput(
    string RawPlate,
    string NormalizedPlate,
    string? VehicleType,
    string? District,
    string? Street,
    string? Notes,
    string? LocationLink,
    DateTime RecordedAt,
    string? RecorderName,
    bool IsMatched);

public interface IRecordingArchiveService
{
    Task<ApiResponse<RecordingArchiveListResponseDto>> GetAsync(
        Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<(byte[] FileBytes, string FileName)> ExportAsync(
        Guid currentUserId,string? fileName, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteAllAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    Task<ApiResponse<ConvertArchiveToDataFileResponseDto>> ConvertToDataFileAsync(
        Guid currentUserId, ConvertArchiveToDataFileRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> UpdateRowAsync(
    long id, Guid currentUserId, UpdateRecordingArchiveRowDto request, CancellationToken cancellationToken = default);

    // داخلي — بيُستدعى من RecordingProcessingJob (أول تفريغ أو إعادة) ومن
    // CreateManualCheckAsync. مش API عام. بيمسح أي صفوف قديمة لنفس الـ Session
    // قبل ما يكتب الجديدة، عشان "التفريغ تاني" يبدأ من جديد زي ما اتفقنا.
    Task ReplaceSessionEntriesAsync(
        Guid sessionId, Guid userId, List<RecordingArchiveEntryInput> entries, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> DeleteRowAsync(
        long id, Guid currentUserId, CancellationToken cancellationToken = default);
}
