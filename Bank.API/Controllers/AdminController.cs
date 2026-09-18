using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AdminDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IAdminService adminService, ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    /// <summary>
    /// Get all group members of the current admin's group with pagination support.
    /// </summary>
    /// <param name="pagination">Pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of group members</returns>

    [HttpGet("users")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<GroupMemberResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyGroupMembers(
        [FromQuery] PaginationParams pagination, CancellationToken cancellationToken)
    {
        var result = await _adminService.GetMyGroupMembersAsync(CurrentUserId, pagination, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Get a specific group member by their ID within the current admin's group.
    /// </summary>
    /// <param name="id">The ID of the group member to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The requested group member or not found</returns>

    [HttpGet("users/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GroupMemberResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyGroupMemberById(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _adminService.GetMyGroupMemberByIdAsync(CurrentUserId, id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Update a specific group member's information within the current admin's group.
    /// </summary>
    /// <param name="id">The ID of the group member to update</param>
    /// <param name="dto">The DTO containing the updated information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated group member or not found</returns>
    [HttpPut("users/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GroupMemberResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyGroupMember(
        Guid id, [FromBody] UpdateGroupMemberDto dto, CancellationToken cancellationToken)
    {
        var result = await _adminService.UpdateMyGroupMemberAsync(CurrentUserId, id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Remove a specific group member from the current admin's group.
    /// </summary>
    /// <param name="id">The ID of the group member to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the operation</returns>
    [HttpDelete("users/{id:guid}/group")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveUserFromMyGroup(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _adminService.RemoveMemberFromMyGroupAsync(CurrentUserId, id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Update a specific group member's permissions within the current admin's group.
    /// </summary>
    /// <param name="id">The ID of the group member whose permissions to update</param>
    /// <param name="dto">The DTO containing the updated permissions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated group member or not found</returns>
    [HttpPut("users/{id:guid}/permissions")]
    [ProducesResponseType(typeof(ApiResponse<UserPermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemberPermissions(
        Guid id, [FromBody] UpdateUserPermissionDto dto, CancellationToken cancellationToken)
    {
        var result = await _adminService.UpdateMemberPermissionsAsync(CurrentUserId, id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}