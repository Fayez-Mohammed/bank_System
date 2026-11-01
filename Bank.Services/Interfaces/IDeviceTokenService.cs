using Bank.Shared.DTOs;
using Bank.Shared.DTOs.SubscriptionDTOs;

public interface IDeviceTokenService
{
    Task<ApiResponse<bool>> RegisterAsync(Guid userId, RegisterDeviceTokenDto dto, CancellationToken ct = default);
    Task<ApiResponse<bool>> RemoveAsync(Guid userId, string token, CancellationToken ct = default);
}