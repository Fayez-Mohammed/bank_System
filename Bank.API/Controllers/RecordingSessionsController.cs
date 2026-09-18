using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.RecordingSessionDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/recording-sessions")]
[Authorize]
public class RecordingSessionsController : ControllerBase
{
    private readonly IRecordingSessionService _recordingService;
    private readonly ILogger<RecordingSessionsController> _logger;

    public RecordingSessionsController(IRecordingSessionService recordingService, ILogger<RecordingSessionsController> logger)
    {
        _recordingService = recordingService;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    //[HttpPost("manualCheck")]
    //public IActionResult ManuaCheck([FromBody] ManualCheckRequestDto request, CancellationToken cancellationToken)
    //{
    //    var result = _recordingService.ManualCheckPlateAsync(CurrentUserId, request, cancellationToken);
    //    return StatusCode(result.Result.StatusCode, result.Result);
    //}
    /// <summary>
    /// Creates a manual check for a plate number with the provided details.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("manual-check")]
    public async Task<IActionResult> CreateManualCheck(
    [FromBody] Bank.Shared.DTOs.RecordingSessionDTOs.CreateManualCheckRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _recordingService.CreateManualCheckAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Creates a new recording session for the current user.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateRecordingSessionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateRecordingSessionRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _recordingService.CreateAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Adds GPS points to an existing recording session for the current user.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("{sessionId:guid}/gps-points")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddGpsPoints(
        Guid sessionId, [FromBody] AddGpsPointsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _recordingService.AddGpsPointsAsync(sessionId, CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Completes an existing recording session for the current user by uploading an audio file.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="audio"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("{sessionId:guid}/complete")]
    [RequestSizeLimit(50_000_000)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(
        Guid sessionId,  IFormFile audio, CancellationToken cancellationToken)
    {
        var result = await _recordingService.CompleteAsync(sessionId, CurrentUserId, audio, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Retries processing an existing recording session for the current user.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("{sessionId:guid}/retry")]
    [ProducesResponseType(typeof(ApiResponse<List<RecordingSessionSummaryDto>>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _recordingService.RetryAsync(sessionId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Retrieves all recording sessions for the current user.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<RecordingSessionSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _recordingService.GetAllAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Retrieves a specific recording session by its ID for the current user.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<RecordingSessionDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _recordingService.GetByIdAsync(sessionId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Retrieves the recorded plates for a specific recording session by its ID for the current user, with pagination support.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{sessionId:guid}/plates")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RecordedPlateRowDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlates(
        Guid sessionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _recordingService.GetPlatesAsync(sessionId, CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Retrieves the audio file for a specific recording session by its ID for the current user.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{sessionId:guid}/audio")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAudio(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var (stream, contentType, fileName) = await _recordingService.GetAudioAsync(sessionId, CurrentUserId, cancellationToken);
            return File(stream, contentType, fileName);
        }
        catch (KeyNotFoundException ex)
        {
            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
            return StatusCode(response.StatusCode, response);
        }
    }
    /// <summary>
    /// Exports the recorded plates for a specific recording session by its ID for the current user as an Excel file.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{sessionId:guid}/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var (bytes, fileName) = await _recordingService.ExportAsync(sessionId, CurrentUserId, cancellationToken);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
            return StatusCode(response.StatusCode, response);
        }
    }
    /// <summary>
    /// Appends data to an existing recording session's data file for the current user.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("{sessionId:guid}/append-to-data-file")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AppendToDataFile(
        Guid sessionId, [FromBody] AppendToDataFileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _recordingService.AppendToDataFileAsync(sessionId, CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Deletes a specific recording session by its ID for the current user.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpDelete("{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _recordingService.DeleteAsync(sessionId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Deletes all recording sessions for the current user.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        var result = await _recordingService.DeleteAllAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}