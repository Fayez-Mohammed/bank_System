using Bank.DAL.Models.BaseModels;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AuthDTOs;
using Bank.Shared.DTOs.UserDTOs;
using Microsoft.AspNetCore.Components.Web;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Interfaces
{
    public interface IUserService
    {
        Task<ApplicationUser> GetByEmailAsync(string email);
        Task<ApplicationUser> GetByIdAsync(string id);
        Task<ApplicationUser> CreateUserAsync(ApplicationUser user,string password);
        Task<bool> CheckPasswordAsync(ApplicationUser user,string password);
        Task<ApiResponse<MyGroupDTO>> GetMyGroupAsync(string userId);
        // Task UpdateProfilePictureAsync(string userId,string imagePath);
    }
}
