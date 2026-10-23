using Bank.DAL.Context;
using Bank.DAL.Models.BaseModels;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AdminDTOs;
using Bank.Shared.DTOs.SuperAdminDTOs;
using Bank.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Implementations
{
    public class SuperAdminService : ISuperAdminService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly AppDbContext _context;
        private readonly IUserService _userService;
        private readonly ILogger<SuperAdminService> _logger;
        public SuperAdminService(
            IUnitOfWork unitOfWork,
            AppDbContext context,
            IUserService userService,
            ILogger<SuperAdminService> logger)
        {
            _unitOfWork = unitOfWork;
            _context = context;
            _logger = logger;
            _userService = userService;
        }

    //Admins
        public async Task<ApiResponse<AdminResponseDto>> CreateAdminAsync(CreateAdminDto dto, CancellationToken ct = default)
        {
            if (await _userService.GetByEmailAsync(dto.Email) is not null)
            {
                _logger.LogWarning("CreateAdmin rejected: email {Email} already taken", dto.Email);
                return ApiResponse<AdminResponseDto>.Fail("Email is already taken.");
            }

            var admin = new ApplicationUser
            {
                FullName = dto.FullName,
                UserName = dto.Email,
                Email = dto.Email,
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // UserManager handles password hashing, security stamp, normalized
            // username/email — never insert a user manually through the context.
            var adminUser = await _userService.CreateUserAsync(admin, dto.Password);
            if (adminUser is null)
            {
                _logger.LogWarning("CreateAdmin failed for {Email}: User creation unsuccessful", dto.Email);
                return ApiResponse<AdminResponseDto>.Fail("Failed to create admin user.");
            }

            _logger.LogInformation("Admin {AdminId} ({Username}) created", admin.Id, admin.UserName);

            return ApiResponse<AdminResponseDto>.SuccessResponse(new AdminResponseDto
            {
                Id = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email!,
                IsActive = admin.IsActive,
                CreatedAt = admin.CreatedAt
            });
        }

        public async Task<ApiResponse<PagedResult<AdminResponseDto>>> GetAdminsAsync(PaginationParams pagination, CancellationToken ct = default)
        {
            
            var query = _context.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.Admin);

            if (!string.IsNullOrWhiteSpace(pagination.SearchTerm))
            {
                var term = pagination.SearchTerm.Trim();
                query = query.Where(u =>
                    u.FullName.ToLower().Contains(term.ToLower()) ||
                   
                    u.Email!.ToLower().Contains(term.ToLower()));
            }

            var totalCount = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((pagination.PageNumber - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .Select(u => new AdminResponseDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email!,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    AdministeredGroupId = u.AdministeredGroup != null ? u.AdministeredGroup.Id : (Guid?)null,
                    AdministeredGroupName = u.AdministeredGroup != null ? u.AdministeredGroup.Name : null
                })
                .ToListAsync(ct);

            return ApiResponse<PagedResult<AdminResponseDto>>.SuccessResponse(new PagedResult<AdminResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        public async Task<ApiResponse<AdminResponseDto>> GetAdminByIdAsync(Guid adminId, CancellationToken ct = default)
        {
            var admin = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == adminId && u.Role == UserRole.Admin)
                .Select(u => new AdminResponseDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email!,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    AdministeredGroupId = u.AdministeredGroup != null ? u.AdministeredGroup.Id : (Guid?)null,
                    AdministeredGroupName = u.AdministeredGroup != null ? u.AdministeredGroup.Name : null
                })
                .FirstOrDefaultAsync(ct);

            if (admin is null)
            {
                _logger.LogWarning("Admin {AdminId} not found", adminId);
                return ApiResponse<AdminResponseDto>.Fail("Admin not found.");
            }

            return ApiResponse<AdminResponseDto>.SuccessResponse(admin);
        }

        public async Task<ApiResponse<AdminResponseDto>> UpdateAdminAsync(Guid adminId, UpdateAdminDto dto, CancellationToken ct = default)
        {
            var admin = await _userService.GetByIdAsync(adminId.ToString());
            if (admin is null || admin.Role != UserRole.Admin)
            {
                _logger.LogWarning("UpdateAdmin failed: {AdminId} not found", adminId);
                return ApiResponse<AdminResponseDto>.Fail("Admin not found.", null, 404);
            }

            admin.FullName = dto.FullName;
            admin.Email = dto.Email;

            // Going through UserManager keeps NormalizedEmail in sync.
            //var result = await _unitOfWork.CompleteAsync()
            //if (!result.Succeeded)
            //{
            //    var errors = result.Errors.Select(e => e.Description).ToList();
            //    _logger.LogWarning("UpdateAdmin {AdminId} failed: {Errors}", adminId, string.Join("; ", errors));
            //    return ApiResponse<AdminResponseDto>.Fail(errors);
            //}
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Admin {AdminId} updated", adminId);

            return ApiResponse<AdminResponseDto>.SuccessResponse(new AdminResponseDto
            {
                Id = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email!,
                IsActive = admin.IsActive,
                CreatedAt = admin.CreatedAt
            });
        }

        public async Task<ApiResponse<bool>> SetAdminToggleActiveStatusAsync(Guid adminId, CancellationToken ct = default)
        {
            var admin = await _userService.GetByIdAsync(adminId.ToString());
            if (admin is null || admin.Role != UserRole.Admin)
            {
                _logger.LogWarning("SetAdminActiveStatus failed: {AdminId} not found", adminId);
                return ApiResponse<bool>.Fail("Admin not found.", null, 404);
            }

            admin.IsActive = !admin.IsActive;
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Admin {AdminId} active status set to {IsActive}", adminId, admin.IsActive);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        //////////////
       //groups

        public async Task<ApiResponse<GroupResponseDto>> CreateGroupAsync(CreateGroupDto dto, CancellationToken ct = default)
        {
            var admin = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == dto.AdminId && u.Role == UserRole.Admin, ct);

            if (admin is null)
            {
                _logger.LogWarning("CreateGroup rejected: AdminId {AdminId} is not a valid Admin", dto.AdminId);
                return ApiResponse<GroupResponseDto>.Fail("The selected user is not a valid Admin.", null, 400);
            }

            var alreadyAdministers = await _context.Groups.AsNoTracking()
                .AnyAsync(g => g.AdminId == dto.AdminId && g.IsActive, ct);

            if (alreadyAdministers)
            {
                _logger.LogWarning("CreateGroup rejected: Admin {AdminId} already administers an active group", dto.AdminId);
                return ApiResponse<GroupResponseDto>.Fail("This admin already administers an active group.", null, 409);
            }

            var group = new Bank.DAL.Models.SystemModels.Group
            {
                Name = dto.Name,
                AdminId = dto.AdminId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Repository<Group>().AddAsync(group);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Group {GroupId} ({GroupName}) created with Admin {AdminId}", group.Id, group.Name, group.AdminId);

            return ApiResponse<GroupResponseDto>.SuccessResponse(new GroupResponseDto
            {
                Id = group.Id,
                Name = group.Name,
                IsActive = group.IsActive,
                AdminId = group.AdminId,
                AdminName = admin.FullName,
                MembersCount = 0,
                CreatedAt = group.CreatedAt
            });
        }

        public async Task<ApiResponse<PagedResult<GroupResponseDto>>> GetGroupsAsync(PaginationParams pagination, CancellationToken ct = default)
        {
            var query = _context.Groups.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(pagination.SearchTerm))
            {
                var term = pagination.SearchTerm.Trim();
                query = query.Where(g => g.Name.ToLower().Contains(term.ToLower()));
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(g => g.CreatedAt)
                .Skip((pagination.PageNumber - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .Select(g => new GroupResponseDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    IsActive = g.IsActive,
                    AdminId = g.AdminId,
                    AdminName = g.Admin.FullName,
                    MembersCount =g.Members.Count(), //_context.Users.Count(u => u.GroupId == g.Id),
                    CreatedAt = g.CreatedAt
                })
                .ToListAsync(ct);

            return ApiResponse<PagedResult<GroupResponseDto>>.SuccessResponse(new PagedResult<GroupResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        public async Task<ApiResponse<GroupResponseDto>> GetGroupByIdAsync(Guid groupId, CancellationToken ct = default)
        {
            var group = await _context.Groups.AsNoTracking()
                .Where(g => g.Id == groupId)
                .Select(g => new GroupResponseDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    IsActive = g.IsActive,
                    AdminId = g.AdminId,
                    AdminName = g.Admin != null ? g.Admin.FullName : null,
                    MembersCount =g.Members.Count(), //_context.Users.Count(u => u.GroupId == g.Id),
                    CreatedAt = g.CreatedAt
                })
                .FirstOrDefaultAsync(ct);

            if (group is null)
            {
                _logger.LogWarning("Group {GroupId} not found", groupId);
                return ApiResponse<GroupResponseDto>.Fail("Group not found.", null, 404);
            }

            return ApiResponse<GroupResponseDto>.SuccessResponse(group);
        }

        public async Task<ApiResponse<GroupResponseDto>> UpdateGroupAsync(Guid groupId, UpdateGroupDto dto, CancellationToken ct = default)
        {
            var repo = _unitOfWork.Repository<Group>();
            var group = await repo.GetByIdAsync(groupId);
            if (group is null)
            {
                _logger.LogWarning("UpdateGroup failed: {GroupId} not found", groupId);
                return ApiResponse<GroupResponseDto>.Fail("Group not found.", null, 404);
            }

            group.Name = dto.Name;
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Group {GroupId} renamed to {GroupName}", groupId, dto.Name);

            return await GetGroupByIdAsync(groupId, ct);
        }

        public async Task<ApiResponse<bool>> SetGroupToggleActiveStatusAsync(Guid groupId, CancellationToken ct = default)
        {
            var repo = _unitOfWork.Repository<Group>();
            var group = await repo.GetByIdAsync(groupId);
            if (group is null)
            {
                _logger.LogWarning("SetGroupActiveStatus failed: {GroupId} not found", groupId);
                return ApiResponse<bool>.Fail("Group not found.", null, 404);
            }

            group.IsActive = !group.IsActive;
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Group {GroupId} active status set to {IsActive}", groupId, group.IsActive);
            return ApiResponse<bool>.SuccessResponse(true);
        }

        public async Task<ApiResponse<GroupResponseDto>> AssignAdminToGroupAsync(Guid groupId, AssignAdminToGroupDto dto, CancellationToken ct = default)
        {
            var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
            if (group is null)
            {
                _logger.LogWarning("AssignAdminToGroup failed: Group {GroupId} not found", groupId);
                return ApiResponse<GroupResponseDto>.Fail("Group not found.", null, 404);
            }

            var newAdmin = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == dto.AdminId && u.Role == UserRole.Admin, ct);

            if (newAdmin is null)
            {
                _logger.LogWarning("AssignAdminToGroup rejected: {AdminId} is not a valid Admin", dto.AdminId);
                return ApiResponse<GroupResponseDto>.Fail("The selected user is not a valid Admin.", null, 400);
            }

            var previousAdminId = group.AdminId;
            group.AdminId = dto.AdminId;
           await _unitOfWork.Repository<Group>().UpdateAsync(group);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Group {GroupId} admin changed from {OldAdminId} to {NewAdminId}", groupId, previousAdminId, dto.AdminId);

            return await GetGroupByIdAsync(groupId, ct);
        }
       // users

        public async Task<ApiResponse<UserResponseDto>> CreateUserAsync(CreateUserDto dto, CancellationToken ct = default)
        {
            if (await _userService.GetByEmailAsync(dto.Email) is not null)
            {
                _logger.LogWarning("CreateUser rejected: email {Email} already taken", dto.Email);
                return ApiResponse<UserResponseDto>.Fail("Email is already taken.", null, 409);
            }

            if (dto.GroupId.HasValue)
            {
                var groupExists = await _context.Groups.AsNoTracking()
                    .AnyAsync(g => g.Id == dto.GroupId.Value && g.IsActive, ct);

                if (!groupExists)
                {
                    _logger.LogWarning("CreateUser rejected: GroupId {GroupId} not found/inactive", dto.GroupId);
                    return ApiResponse<UserResponseDto>.Fail("Group not found or inactive.", null, 400);
                }
            }

            var user = new ApplicationUser
            {
                FullName = dto.FullName,
                Email = dto.Email,
                Role = UserRole.User,
                IsActive = true,
                UserName=dto.Email,
                CreatedAt = DateTime.UtcNow,
                GroupId = dto.GroupId
            };

            var newUser = await _userService.CreateUserAsync(user, dto.Password);
            if (newUser is null)
            {
                _logger.LogWarning("CreateUser failed for {Email}: Failed to create user.", dto.Email);
                return ApiResponse<UserResponseDto>.Fail("Failed to create user.", null, 400);
            }


            //var permission = new UserPermission { UserId = user.Id };
            var permission = dto.Permissions is not null ? new UserPermission { UserId = user.Id,
                CanManageCheckFiles = dto.Permissions.CanManageCheckFiles,
                CanUploadDataFiles = dto.Permissions.CanUploadDataFiles,
                CanSort = dto.Permissions.CanSort
            } : new UserPermission { UserId = user.Id };

            await _unitOfWork.Repository<UserPermission>().AddAsync(permission);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("User {UserId} ({Username}) created, GroupId={GroupId}", user.Id, user.UserName, dto.GroupId);

            string? groupName = dto.GroupId.HasValue
                ? await _context.Groups.AsNoTracking().Where(g => g.Id == dto.GroupId.Value).Select(g => g.Name).FirstOrDefaultAsync(ct)
                : null;

            return ApiResponse<UserResponseDto>.SuccessResponse(new UserResponseDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email!,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                GroupId = user.GroupId,
                GroupName = groupName
            });
        }

        public async Task<ApiResponse<PagedResult<UserResponseDto>>> GetUsersAsync(PaginationParams pagination, Guid? groupId, CancellationToken ct = default)
        {
            var query = _context.Users.AsNoTracking().Where(u => u.Role == UserRole.User);

            if (groupId.HasValue)
            {
                query = query.Where(u => u.GroupId == groupId.Value);
            }

            if (!string.IsNullOrWhiteSpace(pagination.SearchTerm))
            {
                var term = pagination.SearchTerm.Trim();
                query = query.Where(u =>
                    u.FullName.ToLower().Contains(term.ToLower()) ||
                    u.Email!.ToLower().Contains(term.ToLower()));
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((pagination.PageNumber - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .Select(u => new UserResponseDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email!,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    GroupId = u.GroupId,
                    GroupName = u.Group != null ? u.Group.Name : null,
                    Permissions = _context.UserPermissions
                    .Where(p => p.UserId == u.Id)
                    .Select(p => new UserPermissionDto
                    {
                        CanManageCheckFiles = p.CanManageCheckFiles,
                        CanUploadDataFiles = p.CanUploadDataFiles,
                        CanSort = p.CanSort
                    })
                    .FirstOrDefault() ?? new UserPermissionDto()
                })
                .ToListAsync(ct);

            return ApiResponse<PagedResult<UserResponseDto>>.SuccessResponse(new PagedResult<UserResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        public async Task<ApiResponse<UserResponseDto>> GetUserByIdAsync(Guid userId, CancellationToken ct = default)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.Role == UserRole.User)
                .Select(u => new UserResponseDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email!,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    GroupId = u.GroupId,
                    GroupName = u.Group != null ? u.Group.Name : null
                })
                .FirstOrDefaultAsync(ct);

            if (user is null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return ApiResponse<UserResponseDto>.Fail("User not found.", null, 404);
            }

            return ApiResponse<UserResponseDto>.SuccessResponse(user);
        }

        public async Task<ApiResponse<UserResponseDto>> UpdateUserAsync(Guid userId, UpdateUserDto dto, CancellationToken ct = default)
        {
            var user = await _userService.GetByIdAsync(userId.ToString());
            if (user is null || user.Role != UserRole.User)
            {
                _logger.LogWarning("UpdateUser failed: {UserId} not found", userId);
                return ApiResponse<UserResponseDto>.Fail("User not found.", null, 404);
            }

            user.FullName = dto.FullName;
            user.Email = dto.Email;

            //var result = await _userManager.UpdateAsync(user);
            //if (!result.Succeeded)
            //{
            //    var errors = result.Errors.Select(e => e.Description).ToList();
            //    _logger.LogWarning("UpdateUser {UserId} failed: {Errors}", userId, string.Join("; ", errors));
            //    return ApiResponse<UserResponseDto>.Fail("Failed to update user.", null, 400);
            //}
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("User {UserId} updated", userId);
            return await GetUserByIdAsync(userId, ct);
        }

        //public async Task<ApiResponse<bool>> SetUserToggleActiveStatusAsync(Guid userId, CancellationToken ct = default)
        //{

        //    var user = await _userService.GetByIdAsync(userId.ToString());
        //    if (user is null || user.Role != UserRole.User)
        //    {
        //        _logger.LogWarning("SetUserActiveStatus failed: {UserId} not found", userId);
        //        return ApiResponse<bool>.Fail("User not found.", null, 404);
        //    }

        //    user.IsActive = !user.IsActive;
        //    await _unitOfWork.CompleteAsync();

        //    _logger.LogInformation("User {UserId} active status set to {IsActive}", userId, user.IsActive);
        //    return ApiResponse<bool>.SuccessResponse(true);
        //}
        public async Task<ApiResponse<bool>> SetUserToggleActiveStatusAsync(Guid userId, CancellationToken ct = default)
        {
            var user = await _userService.GetByIdAsync(userId.ToString());
            if (user is null || user.Role != UserRole.User)
            {
                _logger.LogWarning("SetUserActiveStatus failed: {UserId} not found", userId);
                return ApiResponse<bool>.Fail("User not found.", null, 404);
            }

            // 1. عكس حالة الـ Active
            user.IsActive = !user.IsActive;

            if (!user.IsActive)
            {
                var refreshTokens = await _context.RefreshTokens
                    .Where(r => r.UserId == userId)
                    .ToListAsync(ct);

                if (refreshTokens.Any())
                {
                    _context.RefreshTokens.RemoveRange(refreshTokens);
                }

              //  await DisconnectUserLiveCheckSessionsAsync(userId);
            }

            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("User {UserId} active status set to {IsActive}", userId, user.IsActive);
            return ApiResponse<bool>.SuccessResponse(true);
        }

        //// دالة مساعدة لقطع الـ SignalR
        //private async Task DisconnectUserLiveCheckSessionsAsync(Guid userId)
        //{
        //    // بنجيب الاتصالات النشطة للمستخدم من الـ LiveCheckConnectionManager
        //    var activeConnections = _connections.GetConnectionsByUserId(userId);

        //    foreach (var connectionId in activeConnections)
        //    {
        //        // نبعت إيفنت للفرونت إند يعرفه إن حسابه اتقفل
        //        await _hubContext.Clients.Client(connectionId).SendAsync("AccountDeactivated", new { message = "تم تعطيل حسابك بواسطة الأدمن." });

        //        // نلغي الـ Session ونقفل الاتصال
        //        var state = _connections.Get(connectionId);
        //        if (state is not null)
        //        {
        //            state.ListenCts.Cancel();
        //            await state.AiSession.CloseAsync(CancellationToken.None);
        //            _connections.Remove(connectionId);
        //        }
        //    }
        //}
        public async Task<ApiResponse<UserResponseDto>> AssignUserToGroupAsync(Guid userId, AssignUserToGroupDto dto, CancellationToken ct = default)
        {
           
            var user = await _userService.GetByIdAsync(userId.ToString());
            if (user is null || user.Role != UserRole.User)
            {
                _logger.LogWarning("AssignUserToGroup failed: User {UserId} not found", userId);
                return ApiResponse<UserResponseDto>.Fail("User not found.", null, 404);
            }

            var groupExists = await _context.Groups.AsNoTracking()
                .AnyAsync(g => g.Id == dto.GroupId && g.IsActive, ct);

            if (!groupExists)
            {
                _logger.LogWarning("AssignUserToGroup rejected: GroupId {GroupId} not found/inactive", dto.GroupId);
                return ApiResponse<UserResponseDto>.Fail("Group not found or inactive.", null, 400);
            }

            var previousGroupId = user.GroupId;
            user.GroupId = dto.GroupId;
            await _unitOfWork.CompleteAsync();

            // أذونات الفرز كانت على ملفات الجروب القديم
            if (previousGroupId != dto.GroupId)
                await _context.UserDataFilePermissions.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);

            _logger.LogInformation("User {UserId} moved from Group {OldGroupId} to Group {NewGroupId}", userId, previousGroupId, dto.GroupId);

            return await GetUserByIdAsync(userId, ct);
        }

        public async Task<ApiResponse<UserResponseDto>> RemoveUserFromGroupAsync(Guid userId, CancellationToken ct = default)
        {
          
            var user = await _userService.GetByIdAsync(userId.ToString());
            if (user is null || user.Role != UserRole.User)
            {
                _logger.LogWarning("RemoveUserFromGroup failed: User {UserId} not found", userId);
                return ApiResponse<UserResponseDto>.Fail("User not found.", null, 404);
            }

            var previousGroupId = user.GroupId;
            user.GroupId = null;
            await _unitOfWork.CompleteAsync();

            // أذونات الفرز كانت على ملفات الجروب ده
            await _context.UserDataFilePermissions.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);

            _logger.LogInformation("User {UserId} removed from Group {GroupId}", userId, previousGroupId);

            return await GetUserByIdAsync(userId, ct);
        }
    }
}
