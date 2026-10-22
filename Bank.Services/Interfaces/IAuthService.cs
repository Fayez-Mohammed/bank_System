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
        Task<ApiResponse<MyGroupDTO>> GetMyGroupAsync(string userId);
    }
}
