using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AuthDTOs;
using Bank.Shared.DTOs.UserDTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Interfaces
{
    public interface IAuthService
    {
        Task<ApiResponse<RegisterResponseDTO>> RegisterAsync(RegisterDTO registerDTO);
        Task<ApiResponse<LoginResponseDTO>> LoginAsync(LoginDTO loginDTO);
        Task<ApiResponse<LoginResponseDTO>> RefreshAsync(RefreshTokenRequestDTO request, CancellationToken ct = default);
        Task<ApiResponse<bool>> LogoutAsync(LogoutRequestDTO request, CancellationToken ct = default);
        Task<ApiResponse<bool>> ResetUserDeviceAsync(Guid actorId, Guid targetUserId, CancellationToken ct = default);

        Task<ApiResponse<MyGroupDTO>> GetMyGroupAsync(string userId);
    }
}
