using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.SuperAdminDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/super-admin/users")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminUsersController : ControllerBase
{
    private readonly ISuperAdminService _superAdminService;
    private readonly ILogger<SuperAdminUsersController> _logger;
    private readonly IAuthService _authService;

    public SuperAdminUsersController(ISuperAdminService superAdminService, ILogger<SuperAdminUsersController> logger, IAuthService authService)
    {
        _superAdminService = superAdminService;
        _logger = logger;
        _authService = authService;
    }
    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpPost("{id:guid}/reset-device")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetUserDevice(Guid id, CancellationToken cancellationToken)
    {
        var result = await _authService.ResetUserDeviceAsync(CurrentUserId, id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Creates a new user with the specified details.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.CreateUserAsync(dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Retrieves a paginated list of users with optional filtering by group ID.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] PaginationParams pagination, [FromQuery] Guid? groupId, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.GetUsersAsync(pagination, groupId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Retrieves a specific user by their unique identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.GetUserByIdAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Updates an existing user's details, such as their name, email, or other attributes.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(
        Guid id, [FromBody] UpdateUserDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.UpdateUserAsync(id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Toggles the active status of a user account, allowing them to access the system.
    /// </summary>
    [HttpPatch("{id:guid}/toggle-active")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleUserActiveStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.SetUserToggleActiveStatusAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

  

    /// <summary>
    /// Assigns a user to a specific group, allowing the user to access resources and functionalities associated with that group.
    /// </summary>
    [HttpPatch("{id:guid}/assign-group")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignUserToGroup(
        Guid id, [FromBody] AssignUserToGroupDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.AssignUserToGroupAsync(id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Removes a user from their assigned group, revoking their access to resources and functionalities associated with that group.
    /// </summary>
    [HttpDelete("{id:guid}/group")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveUserFromGroup(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.RemoveUserFromGroupAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}