using Bank.Shared.DTOs;
using Bank.Shared.DTOs.SuperAdminDTOs;

public interface ISuperAdminService
{
    // ----- Admins -----
    Task<ApiResponse<AdminResponseDto>> CreateAdminAsync(CreateAdminDto dto, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AdminResponseDto>>> GetAdminsAsync(PaginationParams pagination, CancellationToken ct = default);
    Task<ApiResponse<AdminResponseDto>> GetAdminByIdAsync(Guid adminId, CancellationToken ct = default);
    Task<ApiResponse<AdminResponseDto>> UpdateAdminAsync(Guid adminId, UpdateAdminDto dto, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetAdminToggleActiveStatusAsync(Guid adminId, CancellationToken ct = default);

    // ----- Groups -----
    Task<ApiResponse<GroupResponseDto>> CreateGroupAsync(CreateGroupDto dto, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<GroupResponseDto>>> GetGroupsAsync(PaginationParams pagination, CancellationToken ct = default);
    Task<ApiResponse<GroupResponseDto>> GetGroupByIdAsync(Guid groupId, CancellationToken ct = default);
    Task<ApiResponse<GroupResponseDto>> UpdateGroupAsync(Guid groupId, UpdateGroupDto dto, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetGroupToggleActiveStatusAsync(Guid groupId, CancellationToken ct = default);
    Task<ApiResponse<GroupResponseDto>> AssignAdminToGroupAsync(Guid groupId, AssignAdminToGroupDto dto, CancellationToken ct = default);

    // ----- Users -----
    Task<ApiResponse<UserResponseDto>> CreateUserAsync(CreateUserDto dto, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<UserResponseDto>>> GetUsersAsync(PaginationParams pagination, Guid? groupId, CancellationToken ct = default);
    Task<ApiResponse<UserResponseDto>> GetUserByIdAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResponse<UserResponseDto>> UpdateUserAsync(Guid userId, UpdateUserDto dto, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetUserToggleActiveStatusAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResponse<UserResponseDto>> AssignUserToGroupAsync(Guid userId, AssignUserToGroupDto dto, CancellationToken ct = default);
    Task<ApiResponse<UserResponseDto>> RemoveUserFromGroupAsync(Guid userId, CancellationToken ct = default);
}