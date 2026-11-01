using Bank.DAL.Context;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.SubscriptionDTOs;
using Microsoft.EntityFrameworkCore;

namespace Bank.Services.Implementations;

public class DeviceTokenService : IDeviceTokenService
{
    private readonly AppDbContext _db;
    public DeviceTokenService(AppDbContext db) => _db = db;

    public async Task<ApiResponse<bool>> RegisterAsync(Guid userId, RegisterDeviceTokenDto dto, CancellationToken ct = default)
    {
        var token = dto.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024)
            return ApiResponse<bool>.Fail("FCM token غير صالح.");

        // نفس الجهاز ممكن يكون كان مسجل لحساب تاني → نشيله منه الأول
        await _db.Users
            .Where(u => u.FcmToken == token && u.Id != userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FcmToken, (string?)null)
                .SetProperty(u => u.FcmTokenUpdatedAt, (DateTime?)null), ct);

        var updated = await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FcmToken, token)
                .SetProperty(u => u.FcmTokenUpdatedAt, DateTime.UtcNow), ct);

        return updated == 0
            ? ApiResponse<bool>.Fail("المستخدم غير موجود.", null, 404)
            : ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<bool>> RemoveAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var value = token?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return ApiResponse<bool>.SuccessResponse(true);

        await _db.Users
            .Where(u => u.Id == userId && u.FcmToken == value)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FcmToken, (string?)null)
                .SetProperty(u => u.FcmTokenUpdatedAt, (DateTime?)null), ct);

        return ApiResponse<bool>.SuccessResponse(true);
    }
}