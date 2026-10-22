using Bank.DAL.Models.BaseModels;
using Bank.Repo.Implementations;
using Bank.Repo.Interfaces;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs.AuthDTOs;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;
using Bank.Shared.Enums;
using Bank.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Bank.DAL.Models.BaseModels;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs.UserDTOs;
using Bank.DAL.Context;

namespace Bank.Services.Implementations
{
    public class UserService : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;
        private readonly AppDbContext _dbContext;
        private readonly IUnitOfWork _unitOfWork;
        public UserService(UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            IUnitOfWork unitOfWork,
            AppDbContext dbContext
            )
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _unitOfWork = unitOfWork;
            _dbContext = dbContext;
        }
        public async Task<ApplicationUser> GetByEmailAsync(string email)
        =>await _userManager.FindByEmailAsync(email);
        public async Task<ApplicationUser> GetByIdAsync(string Id)
        =>await _userManager.FindByIdAsync(Id);

       



        public async Task<ApplicationUser> CreateUserAsync(ApplicationUser user,string password)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNullOrWhiteSpace(password);
            var result=await  _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));
            result = await _userManager.AddToRoleAsync(user, user.Role.ToString());
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));

            return user;
        }

        public async Task<bool> CheckPasswordAsync(ApplicationUser user, string password)
        {
            bool Iscorrect=await _userManager.CheckPasswordAsync(user, password);
            return Iscorrect;
        }
        public async Task<ApiResponse<MyGroupDTO>> GetMyGroupAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException("User not found.");
            if (user.Role == UserRole.User)
            {
                var group = await _dbContext.Groups
                    .Where(g => g.Id == user.GroupId)
                    .Select(g => new MyGroupDTO
                    {
                        IsInGroup = user.GroupId != null,
                        UserRole = user.Role.ToString(),
                        GroupId = g.Id.ToString(),
                        GroupName = g.Name
                    })
                    .ToListAsync();
                var response = new ApiResponse<MyGroupDTO>
                {
                    Data = group.FirstOrDefault(),
                    Success = true,
                    Message = "User group retrieved successfully."
                };
                return response;
            }
            else if (user.Role == UserRole.Admin)
            {
                var group = await _dbContext.Groups
                    .Where(g=>g.AdminId==user.Id)
                    .Select(g => new MyGroupDTO
                    {
                        IsInGroup = true,
                        UserRole = user.Role.ToString(),
                        GroupId = g.Id.ToString(),
                        GroupName = g.Name
                    })
                    .ToListAsync();
                var response = new ApiResponse<MyGroupDTO>
                {
                    Data = group.FirstOrDefault(),
                    Success = true,
                    Message = "Admin group retrieved successfully."
                };
                return response;
            }
            else
            {
                throw new InvalidOperationException("Invalid user role.");
            }
        
           
        }

        //public async Task UpdateProfilePictureAsync(string userId, string imagePath)
        //{
        //    var user =await _userManager.FindByIdAsync(userId);
        //    user.ImagePath = imagePath;
        //  await  _unitOfWork.CompleteAsync();

        //}


    }
}
