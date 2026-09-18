using Bank.Shared.DTOs;
using Bank.Shared.DTOs.SuperAdminDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/super-admin/admins")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminAdminsController : ControllerBase
{
    private readonly ISuperAdminService _superAdminService;
    private readonly ILogger<SuperAdminAdminsController> _logger;

    public SuperAdminAdminsController(ISuperAdminService superAdminService, ILogger<SuperAdminAdminsController> logger)
    {
        _superAdminService = superAdminService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new admin.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAdmin(
        [FromBody] CreateAdminDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.CreateAdminAsync(dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Retrieves a paginated list of admins.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAdmins(
        [FromQuery] PaginationParams pagination, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.GetAdminsAsync(pagination, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Retrieves an admin by their unique identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdminById(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.GetAdminByIdAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Updates an existing admin's details.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAdmin(
        Guid id, [FromBody] UpdateAdminDto dto, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.UpdateAdminAsync(id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Toggles the active status of an admin account.
    /// </summary>
    [HttpPatch("{id:guid}/toggle-active")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleAdminActiveStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _superAdminService.SetAdminToggleActiveStatusAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

   
}