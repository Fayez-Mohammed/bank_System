using Bank.Shared.DTOs;
using Bank.Shared.DTOs.MapDTOs;

namespace Bank.Services.Interfaces;

public interface IMapService
{
    // زر "الأحدث" — أحدث عملية فرز مكتملة في مساحة عمل المستخدم (جروب أو شخصي).
    // بيقارن JobId الأحدث مع الـ cache، ولو مختلف بيجهّز نسخة جديدة وقت الطلب.
    Task<ApiResponse<MapResultDto>> GetLatestAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    // زر "الملف المرفوع" — متاح لكل أعضاء الجروب للعرض.
    Task<ApiResponse<MapUploadedFileDto>> GetUploadedFileAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    // رفع/استبدال الملف — أدمن الجروب بس (أو اليوزر المستقل نفسه لمساحته الشخصية).
    Task<ApiResponse<MapUploadedFileDto>> UploadFileAsync(
        Guid currentUserId, UploadMapFileRequestDto request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteUploadedFileAsync(Guid currentUserId, CancellationToken cancellationToken = default);
}
