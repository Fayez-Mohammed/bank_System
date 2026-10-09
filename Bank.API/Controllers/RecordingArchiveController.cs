using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.RecordingArchiveDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/recording-archive")]
[Authorize]
public class RecordingArchiveController : ControllerBase
{
    private readonly IRecordingArchiveService _archiveService;
    private readonly ILogger<RecordingArchiveController> _logger;

    public RecordingArchiveController(IRecordingArchiveService archiveService, ILogger<RecordingArchiveController> logger)
    {
        _archiveService = archiveService;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    /// <summary>
    /// Updates a single row in the recording archive directly.
    /// </summary>
    [HttpPatch("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRow(
        long id, [FromBody] UpdateRecordingArchiveRowDto request, CancellationToken cancellationToken)
    {
        var result = await _archiveService.UpdateRowAsync(id, CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Gets the current user's recording archive — a persistent, ever-growing table of every
    /// plate transcribed from any recording session or manual check. Unaffected by deleting
    /// recording sessions or clearing them.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<RecordingArchiveListResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _archiveService.GetAsync(CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Exports the full recording archive to an Excel file, in the same layout used for
    /// recording session exports.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromQuery] string? fileName = null, CancellationToken cancellationToken = default)
    {
        var (bytes, resultFileName) = await _archiveService.ExportAsync(CurrentUserId, fileName, cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", resultFileName);
    }

    /// <summary>
    /// Permanently clears the entire recording archive table for the current user.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        var result = await _archiveService.DeleteAllAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Converts the archive's not-yet-converted rows into a data file — either appended to an
    /// existing data file (ExistingDataFileId) or as a brand new one (NewDataFileName). The
    /// archive itself is never cleared by this; it keeps accumulating and can be converted again later.
    /// </summary>
    [HttpPost("convert-to-data-file")]
    [Authorize(Policy = "CanUploadDataFiles")]
    [ProducesResponseType(typeof(ApiResponse<ConvertArchiveToDataFileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConvertToDataFile(
        [FromBody] ConvertArchiveToDataFileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _archiveService.ConvertToDataFileAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Permanently deletes a single row from the recording archive for the current user.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteRow(
        [FromRoute] long id, CancellationToken cancellationToken)
    {
        var result = await _archiveService.DeleteRowAsync(id, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
