using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs.AdminDTOs;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bank.Services.Implementations;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _dbContext;

    public PermissionService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> CanManageCheckFilesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(userId, cancellationToken);
        return access is not null && (access.Role != UserRole.User || access.CanManageCheckFiles);
    }

    public async Task<bool> CanUploadDataFilesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(userId, cancellationToken);
        return access is not null && (access.Role != UserRole.User || access.CanUploadDataFiles);
    }

    public async Task<bool> CanManageDataFileAsync(Guid userId, Guid dataFileId, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(userId, cancellationToken);
        if (access is null) return false;

        var file = await _dbContext.DataFiles
            .AsNoTracking()
            .Where(f => f.Id == dataFileId)
            .Select(f => new { f.OwnerId, f.GroupId })
            .FirstOrDefaultAsync(cancellationToken);
        if (file is null) return false;

        return access.Role switch
        {
            UserRole.SuperAdmin => true,
            // النطاق الإداري: ملفاته الشخصية + ملفات الجروب اللي بيديره (أو المربوط بيه)
            UserRole.Admin => file.OwnerId == userId
                || (file.GroupId.HasValue && (file.GroupId == access.GroupId || file.GroupId == access.AdministeredGroupId)),
            _ => access.CanUploadDataFiles && file.OwnerId == userId
        };
    }

    public async Task<string?> GetSortDeniedReasonAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(userId, cancellationToken);
        if (access is null) return "المستخدم غير موجود.";
        if (access.Role != UserRole.User) return null;
        if (!access.CanSort) return "ليس لديك صلاحية الفرز.";

        var hasAnyFile = await _dbContext.UserDataFilePermissions
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId, cancellationToken);

        return hasAnyFile ? null : "مفيش أي ملف داتا مسموحلك تفرز عليه. اطلب من الأدمن يحددلك ملفات الفرز.";
    }

    public async Task<MyPermissionsDto?> GetMyPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await GetAccessAsync(userId, cancellationToken);
        if (access is null) return null;

        if (access.Role != UserRole.User)
        {
            return new MyPermissionsDto
            {
                Role = access.Role.ToString(),
                CanManageCheckFiles = true,
                CanUploadDataFiles = true,
                CanSort = true
            };
        }

        var sortableIds = access.CanSort
            ? await _dbContext.UserDataFilePermissions
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => p.DataFileId)
                .ToListAsync(cancellationToken)
            : new List<Guid>();

        return new MyPermissionsDto
        {
            Role = access.Role.ToString(),
            CanManageCheckFiles = access.CanManageCheckFiles,
            CanUploadDataFiles = access.CanUploadDataFiles,
            CanSort = access.CanSort,
            SortableDataFileIds = sortableIds
        };
    }

    private async Task<UserAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new UserAccess(
                u.Role,
                u.GroupId,
                _dbContext.Groups.Where(g => g.AdminId == u.Id && g.IsActive).Select(g => (Guid?)g.Id).FirstOrDefault(),
                u.UserPermission != null && u.UserPermission.CanManageCheckFiles,
                u.UserPermission != null && u.UserPermission.CanUploadDataFiles,
                u.UserPermission != null && u.UserPermission.CanSort))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private record UserAccess(
        UserRole Role, Guid? GroupId, Guid? AdministeredGroupId,
        bool CanManageCheckFiles, bool CanUploadDataFiles, bool CanSort);
}
