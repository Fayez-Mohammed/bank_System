using Bank.Shared.DTOs;
using Bank.Shared.DTOs.SuperAdminDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/super-admin/groups")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminGroupsController : ControllerBase
{
    private readonly ISuperAdminService _superAdminService;
    private readonly ILogger<SuperAdminGroupsController> _logger;

    public SuperAdminGroupsController(ISuperAdminService superAdminService, ILogger<SuperAdminGroupsController> logger)
    {
        _superAdminService = superAdminService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new group with the specified details.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<GroupResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateGroup(
        [FromBody] CreateGroupDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.CreateGroupAsync(dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Retrieves a paginated list of groups with optional search and filtering capabilities.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<GroupResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetGroups(
        [FromQuery] PaginationParams pagination, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.GetGroupsAsync(pagination, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Retrieves a specific group by its unique identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GroupResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGroupById(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.GetGroupByIdAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Updates the details of an existing group, such as its name or other attributes.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GroupResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGroup(
        Guid id, [FromBody] UpdateGroupDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.UpdateGroupAsync(id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Toggles the active status of a group, allowing its members to access associated resources and functionalities.
    /// </summary>
    [HttpPatch("{id:guid}/toggle-active")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleGroupActiveStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.SetGroupToggleActiveStatusAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

 

    /// <summary>
    /// Assigns an admin to a specific group, allowing the admin to manage the group's members and settings.
    /// </summary>
    [HttpPatch("{id:guid}/assign-admin")]
    [ProducesResponseType(typeof(ApiResponse<GroupResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignAdminToGroup(
        Guid id, [FromBody] AssignAdminToGroupDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.AssignAdminToGroupAsync(id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}