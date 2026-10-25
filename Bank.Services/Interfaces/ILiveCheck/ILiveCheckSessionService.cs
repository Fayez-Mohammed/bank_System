using Bank.DAL.Models.SystemModels;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.LiveCheckDTOs;
using Bank.Shared.Enums;

namespace Bank.Services.Interfaces;

public interface ILiveCheckSessionService
{
    // ⭐ الميثودز دي بتتنادى من الـ SignalR Hub (streaming lifecycle)
    Task<(CheckSession Session, Guid? CheckFileId)> CreateSessionAsync(
        Guid userId, string? recorderName, string? district, string? street, string? excelSheetName, CancellationToken cancellationToken = default);

    Task<LiveCheckPlateResultDto> ProcessDetectionAsync(
        Guid sessionId, Guid userId, Guid? groupId, Guid? checkFileId, string? district, string? street,
        double? latitude, double? longitude, AiPlateDetectedEvent detection, CheckType checkType,
        CancellationToken cancellationToken = default);

    // الـ AI بيبعت تصحيح لآخر لوحة اتسمعت في الجلسة (من غير ما يحدد رقمها
    // القديم). null = مفيش لوحة سابقة في الجلسة يتصحّح عليها (بيتسجّل تحذير
    // في اللوج، مفيش حاجة تترجع للفلاتر).
    Task<LiveCheckPlateResultDto?> ProcessCorrectionAsync(
        Guid sessionId, Guid? checkFileId, string newPlateNumber, string? vehicleType, CancellationToken cancellationToken = default);

    // الـ AI بيبعت استبدال برقم اللوحة القديم صراحة — بندوّر عليها جوه نفس
    // الـ Session بس (تحقق أمان: مينفعش يعدّل سجل جلسة تانية). null = مفيش
    // سجل بالرقم القديم ده في الجلسة دي.
    Task<LiveCheckPlateResultDto?> ProcessReplacementAsync(
        Guid sessionId, Guid? checkFileId, string oldPlateNumber, string newPlateNumber, string? vehicleType,
        CancellationToken cancellationToken = default);

    Task CompleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task MarkSessionFailedAsync(Guid sessionId, string error, CancellationToken cancellationToken = default);

    // ⭐ الميثودز دي REST عادية (تاريخ، نتائج، تصدير، حذف)
    Task<ApiResponse<ManualLiveCheckResultDto>> CreateManualCheckAsync(
        Guid userId, CreateManualLiveCheckRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<List<CheckSessionSummaryDto>>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResult<LiveCheckLogRowDto>>> GetPlatesAsync(
        Guid sessionId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(byte[] FileBytes, string FileName)> ExportAsync(
    List<Guid> sessionIds, Guid userId, CancellationToken cancellationToken = default);
    Task<(byte[] FileBytes, string FileName)> ExportAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

    Task<ApiResponse<int>> DeleteAllAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LiveCheckLogRowDto>>> GetAllPlatesAsync(
    Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<(byte[] FileBytes, string FileName)> ExportAllAsync(
        Guid userId, CancellationToken cancellationToken = default);

    // كل لوحات التشييك اللي لسه ما اتحوّلتش → append على ملف داتا موجود أو ملف داتا جديد
    // (زي IRecordingArchiveService.ConvertToDataFileAsync بالظبط).
    Task<ApiResponse<ConvertCheckLogsToDataFileResponseDto>> ConvertToDataFileAsync(
        Guid userId, ConvertCheckLogsToDataFileRequestDto request, CancellationToken cancellationToken = default);
}
