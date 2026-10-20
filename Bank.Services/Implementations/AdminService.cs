using Bank.DAL.Context;
using Bank.DAL.Models.BaseModels;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AdminDTOs;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

public class AdminService : IAdminService
{
    private IUserService _userService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDbContext _context;
    private readonly ILogger<AdminService> _logger;

    public AdminService(IUnitOfWork unitOfWork, AppDbContext context, ILogger<AdminService> logger,IUserService userService)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _logger = logger;
        _userService = userService;
    }


    private async Task<Guid?> GetMyGroupIdAsync(Guid currentAdminId, CancellationToken ct)
    {
        return await _context.Groups
            .AsNoTracking()
            .Where(g => g.AdminId == currentAdminId && g.IsActive)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ApiResponse<PagedResult<GroupMemberResponseDto>>> GetMyGroupMembersAsync(
        Guid currentAdminId, PaginationParams pagination, CancellationToken ct = default)
    {
        var myGroupId = await GetMyGroupIdAsync(currentAdminId, ct);
        if (myGroupId is null)
        {
            _logger.LogWarning("Admin {AdminId} attempted to list members but administers no active group", currentAdminId);
            return ApiResponse<PagedResult<GroupMemberResponseDto>>.Fail(
                "You do not currently administer any active group.");
        }

        var query = _context.Users.AsNoTracking()
            .Where(u => u.GroupId == myGroupId && u.Role == UserRole.User);

        if (!string.IsNullOrWhiteSpace(pagination.SearchTerm))
        {
            var term = pagination.SearchTerm.Trim();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term.ToLower()));
        }

        var totalCount = await query.CountAsync(ct);

     
        var items = await query
            .OrderBy(u => u.FullName)
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new GroupMemberResponseDto
            {
                Id = u.Id,
                FullName = u.FullName,
               
                Email = u.Email!,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,

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

        return ApiResponse<PagedResult<GroupMemberResponseDto>>.SuccessResponse(new PagedResult<GroupMemberResponseDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        });
    }

    public async Task<ApiResponse<GroupMemberResponseDto>> GetMyGroupMemberByIdAsync(
        Guid currentAdminId, Guid targetUserId, CancellationToken ct = default)
    {
        var myGroupId = await GetMyGroupIdAsync(currentAdminId, ct);
        if (myGroupId is null)
        {
            return ApiResponse<GroupMemberResponseDto>.Fail(
                "You do not currently administer any active group.");
        }

        var member = await _context.Users.AsNoTracking()
            .Where(u => u.Id == targetUserId)
            .Select(u => new GroupMemberResponseDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email!,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
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
            .FirstOrDefaultAsync(ct);


        var belongsToMyGroup = await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == targetUserId && u.GroupId == myGroupId, ct);

        if (member is null || !belongsToMyGroup)
        {
            _logger.LogWarning("Admin {AdminId} tried to access User {UserId} outside their group", currentAdminId, targetUserId);
            return ApiResponse<GroupMemberResponseDto>.Fail("User not found in your group.");
        }

        return ApiResponse<GroupMemberResponseDto>.SuccessResponse(member);
    }

    public async Task<ApiResponse<GroupMemberResponseDto>> UpdateMyGroupMemberAsync(
        Guid currentAdminId, Guid targetUserId, UpdateGroupMemberDto dto, CancellationToken ct = default)
    {
        var myGroupId = await GetMyGroupIdAsync(currentAdminId, ct);
        if (myGroupId is null)
        {
            return ApiResponse<GroupMemberResponseDto>.Fail(
                "You do not currently administer any active group.");
        }

        var user = await _userService.GetByIdAsync(targetUserId.ToString());

        if (user is null || user.GroupId != myGroupId)
        {
            _logger.LogWarning("Admin {AdminId} tried to update User {UserId} outside their group", currentAdminId, targetUserId);
            return ApiResponse<GroupMemberResponseDto>.Fail("User not found in your group.");
        }

        user.FullName = dto.FullName;
        user.Email = dto.Email;
      
        await _unitOfWork.CompleteAsync();

        _logger.LogInformation("Admin {AdminId} updated User {UserId} in Group {GroupId}", currentAdminId, targetUserId, myGroupId);

        return await GetMyGroupMemberByIdAsync(currentAdminId, targetUserId, ct);
    }

    public async Task<ApiResponse<bool>> RemoveMemberFromMyGroupAsync(
        Guid currentAdminId, Guid targetUserId, CancellationToken ct = default)
    {
        var myGroupId = await GetMyGroupIdAsync(currentAdminId, ct);
        if (myGroupId is null)
        {
            return ApiResponse<bool>.Fail("You do not currently administer any active group.");
        }

        var user = await _userService.GetByIdAsync(targetUserId.ToString());

        if (user is null || user.GroupId != myGroupId)
        {
         
            _logger.LogWarning("Admin {AdminId} tried to remove User {UserId} outside their group", currentAdminId, targetUserId);
            return ApiResponse<bool>.Fail("User not found in your group.");
        }

   
        user.GroupId = null;
    
        await _unitOfWork.CompleteAsync();

        // أذونات الفرز كانت على ملفات الجروب ده — مبقتش ليها لازمة
        await _context.UserDataFilePermissions.Where(p => p.UserId == targetUserId).ExecuteDeleteAsync(ct);

        _logger.LogInformation("Admin {AdminId} removed User {UserId} from Group {GroupId}", currentAdminId, targetUserId, myGroupId);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<UserPermissionDto>> UpdateMemberPermissionsAsync(
        Guid currentAdminId, Guid targetUserId, UpdateUserPermissionDto dto, CancellationToken ct = default)
    {
        var myGroupId = await GetMyGroupIdAsync(currentAdminId, ct);
        if (myGroupId is null)
        {
            return ApiResponse<UserPermissionDto>.Fail(
                "You do not currently administer any active group.");
        }

        var userInGroup = await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == targetUserId && u.GroupId == myGroupId, ct);

        if (!userInGroup)
        {
            _logger.LogWarning("Admin {AdminId} tried to change permissions for User {UserId} outside their group", currentAdminId, targetUserId);
            return ApiResponse<UserPermissionDto>.Fail("User not found in your group.");
        }

        var permRepo = _unitOfWork.Repository<UserPermission>();
        var permission = await _context.UserPermissions.FirstOrDefaultAsync(p => p.UserId == targetUserId, ct);

        if (permission is null)
        {
            
            _logger.LogWarning("UserPermission row missing for User {UserId}; creating one now", targetUserId);
            permission = new UserPermission { UserId = targetUserId };
            await permRepo.AddAsync(permission);
        }

  
        permission.CanManageCheckFiles = dto.CanManageCheckFiles;
        permission.CanUploadDataFiles = dto.CanUploadDataFiles;
        permission.CanSort = dto.CanSort;

       await permRepo.UpdateAsync(permission);
        await _unitOfWork.CompleteAsync();

        _logger.LogInformation("Admin {AdminId} updated permissions for User {UserId} in Group {GroupId}", currentAdminId, targetUserId, myGroupId);

        return ApiResponse<UserPermissionDto>.SuccessResponse(dto);
    }

    public async Task<ApiResponse<List<UserDataFilePermissionDto>>> GetMemberDataFilePermissionsAsync(
        Guid currentAdminId, Guid targetUserId, CancellationToken ct = default)
    {
        var myGroupId = await GetMyGroupIdAsync(currentAdminId, ct);
        if (myGroupId is null)
        {
            return ApiResponse<List<UserDataFilePermissionDto>>.Fail(
                "You do not currently administer any active group.");
        }

        var userInGroup = await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == targetUserId && u.GroupId == myGroupId && u.Role == UserRole.User, ct);

        if (!userInGroup)
        {
            return ApiResponse<List<UserDataFilePermissionDto>>.Fail("User not found in your group.", null, 404);
        }

        var items = await _context.UserDataFilePermissions.AsNoTracking()
            .Where(p => p.UserId == targetUserId)
            .OrderBy(p => p.DataFile.SortOrder)
            .Select(p => new UserDataFilePermissionDto
            {
                DataFileId = p.DataFileId,
                FileName = p.DataFile.FileName,
                GrantedByAdminId = p.GrantedByAdminId,
                GrantedByAdminName = p.GrantedByAdmin.FullName,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(ct);

        return ApiResponse<List<UserDataFilePermissionDto>>.SuccessResponse(items);
    }

    public async Task<ApiResponse<List<UserDataFilePermissionDto>>> UpdateMemberDataFilePermissionsAsync(
        Guid currentAdminId, Guid targetUserId, UpdateUserDataFilePermissionsDto dto, CancellationToken ct = default)
    {
        var myGroupId = await GetMyGroupIdAsync(currentAdminId, ct);
        if (myGroupId is null)
        {
            return ApiResponse<List<UserDataFilePermissionDto>>.Fail(
                "You do not currently administer any active group.");
        }

        var userInGroup = await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == targetUserId && u.GroupId == myGroupId && u.Role == UserRole.User, ct);

        if (!userInGroup)
        {
            _logger.LogWarning("Admin {AdminId} tried to change data file permissions for User {UserId} outside their group", currentAdminId, targetUserId);
            return ApiResponse<List<UserDataFilePermissionDto>>.Fail("User not found in your group.", null, 404);
        }

        var requestedIds = dto.DataFileIds.Distinct().ToList();

        // الأدمن يقدر يدّي إذن بس على ملفات الداتا اللي في جروبه
        var validIds = await _context.DataFiles.AsNoTracking()
            .Where(f => requestedIds.Contains(f.Id) && f.GroupId == myGroupId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        if (validIds.Count != requestedIds.Count)
        {
            return ApiResponse<List<UserDataFilePermissionDto>>.Fail("Some data files were not found in your group.", null, 400);
        }

        var existing = await _context.UserDataFilePermissions
            .Where(p => p.UserId == targetUserId)
            .ToListAsync(ct);

        _context.UserDataFilePermissions.RemoveRange(existing.Where(p => !validIds.Contains(p.DataFileId)));

        var existingIds = existing.Select(p => p.DataFileId).ToHashSet();
        _context.UserDataFilePermissions.AddRange(validIds
            .Where(id => !existingIds.Contains(id))
            .Select(id => new UserDataFilePermission
            {
                UserId = targetUserId,
                DataFileId = id,
                GrantedByAdminId = currentAdminId
            }));

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminId} set {Count} sortable data files for User {UserId} in Group {GroupId}",
            currentAdminId, validIds.Count, targetUserId, myGroupId);

        return await GetMemberDataFilePermissionsAsync(currentAdminId, targetUserId, ct);
    }
}
