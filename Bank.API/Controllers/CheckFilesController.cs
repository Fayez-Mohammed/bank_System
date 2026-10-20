using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CheckFileDTos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/check-files")]
[Authorize]
public class CheckFilesController : ControllerBase
{
    private readonly ICheckFileService _checkFileService;
    private readonly ILogger<CheckFilesController> _logger;

    public CheckFilesController(ICheckFileService checkFileService, ILogger<CheckFilesController> logger)
    {
        _checkFileService = checkFileService;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    /// <summary>
    /// Get all check files for the current user
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<CheckFileSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _checkFileService.GetCheckFilesAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Upload a new check file for the current user
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost]
    [RequestSizeLimit(50_000_000)]
    [ProducesResponseType(typeof(ApiResponse<CheckFileSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload([FromForm] CreateCheckFileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _checkFileService.UploadAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Toggle the active status of a check file for the current user
    /// </summary>
    /// <param name="checkFileId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPatch("{checkFileId:guid}/toggle-active")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleActive(Guid checkFileId, CancellationToken cancellationToken)
    {
        var result = await _checkFileService.ToggleActiveAsync(checkFileId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Delete a check file for the current user
    /// </summary>
    /// <param name="checkFileId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpDelete("{checkFileId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid checkFileId, CancellationToken cancellationToken)
    {
        var result = await _checkFileService.DeleteAsync(checkFileId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}