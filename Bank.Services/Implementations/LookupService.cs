using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.LookupDTOs;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Implementations
{
    public class LookupService : ILookupService
    {
        private AppDbContext _dbContext;
        public LookupService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ApiResponse<List<UserLookupDto>>> GetAdminsAsync(string? searchTerm, CancellationToken cancellationToken)
        {
            var users = await _dbContext.Users
                         .Where(u => u.FullName.ToLower().Contains((searchTerm ?? "").ToLower()) && u.Role == UserRole.Admin && u.IsActive)
                         .Select(u => new UserLookupDto
                         {
                             Id = u.Id,
                             Name = u.FullName
                         })
                         .ToListAsync(cancellationToken);
            return ApiResponse<List<UserLookupDto>>.SuccessResponse(users);
        }

        public async Task<ApiResponse<List<GroupLookupDto>>> GetGroupsAsync(string? searchTerm, CancellationToken cancellationToken)
        {
            var groups = await _dbContext.Groups
                .Where(g => g.Name.ToLower().Contains((searchTerm ?? "").ToLower()))
                .Select(g => new GroupLookupDto
                {
                    Id = g.Id,
                    Name = g.Name
                })
                .ToListAsync(cancellationToken);
            return  ApiResponse<List<GroupLookupDto>>.SuccessResponse(groups);
        }

        public async Task<ApiResponse<List<UserLookupDto>>> GetUsersAsync(string? searchTerm, CancellationToken cancellationToken)
        {
           
            var users = await _dbContext.Users
                .Where(u => u.FullName.ToLower().Contains((searchTerm ?? "").ToLower())&&u.Role==UserRole.User&&u.IsActive)
                .Select(u => new UserLookupDto
                {
                    Id = u.Id,
                    Name = u.FullName
                })
                .ToListAsync(cancellationToken);
            return ApiResponse<List<UserLookupDto>>.SuccessResponse(users);
        }
        }
    }
