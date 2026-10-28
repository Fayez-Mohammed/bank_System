using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AdminDTOs;

public interface IAdminService
{
    Task<ApiResponse<PagedResult<MatchedPlateRowDto>>> GetGroupMatchedPlatesAsync(
    Guid currentAdminId, PaginationParams pagination, CancellationToken ct = default);

    Task<ApiResponse<PagedResult<MatchedPlateRowDto>>> GetUserMatchedPlatesAsync(
        Guid currentAdminId, Guid targetUserId, PaginationParams pagination, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<GroupMemberResponseDto>>> GetMyGroupMembersAsync(
        Guid currentAdminId, PaginationParams pagination, CancellationToken ct = default);

    Task<ApiResponse<GroupMemberResponseDto>> GetMyGroupMemberByIdAsync(
        Guid currentAdminId, Guid targetUserId, CancellationToken ct = default);

    Task<ApiResponse<GroupMemberResponseDto>> UpdateMyGroupMemberAsync(
        Guid currentAdminId, Guid targetUserId, UpdateGroupMemberDto dto, CancellationToken ct = default);

    Task<ApiResponse<bool>> RemoveMemberFromMyGroupAsync(
        Guid currentAdminId, Guid targetUserId, CancellationToken ct = default);

    Task<ApiResponse<UserPermissionDto>> UpdateMemberPermissionsAsync(
        Guid currentAdminId, Guid targetUserId, UpdateUserPermissionDto dto, CancellationToken ct = default);

    Task<ApiResponse<List<UserDataFilePermissionDto>>> GetMemberDataFilePermissionsAsync(
        Guid currentAdminId, Guid targetUserId, CancellationToken ct = default);

    Task<ApiResponse<List<UserDataFilePermissionDto>>> UpdateMemberDataFilePermissionsAsync(
        Guid currentAdminId, Guid targetUserId, UpdateUserDataFilePermissionsDto dto, CancellationToken ct = default);
}
