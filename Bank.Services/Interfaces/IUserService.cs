using Bank.DAL.Models.BaseModels;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AuthDTOs;
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
       // Task UpdateProfilePictureAsync(string userId,string imagePath);
    }
}
