using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.RecordingArchiveDTOs;
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
    /// <summary>
    /// Creates a manual check for a specific plate number. This is useful for situations where the automatic transcription or matching process fails, and a manual intervention is required. The request should include the district, street, plate number, and optionally the vehicle type and Excel sheet name. The response will indicate whether the manual check was successfully created.
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
    /// Creates a new recording session. The request should include the district, street, and other relevant information. The response will contain the ID of the newly created session and its initial status.
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
    /// Updates a recording session's district/street. Blocked while the session is still
    /// Processing. Existing RecordedPlates for this session are updated too, so a subsequent
    /// "retry" transcription (and its archive entry) reflects the new street name.
    /// </summary>
    [HttpPatch("{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid sessionId, [FromBody] UpdateRecordingSessionRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _recordingService.UpdateAsync(sessionId, CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Adds GPS points to a recording session. This is useful for tracking the location of the recording and can be used for mapping or analysis purposes. The GPS points should be provided in a list, with each point containing latitude, longitude, and a timestamp.
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
    /// Completes a recording session by uploading the audio file. This triggers the transcription process and marks the session as completed. The audio file should be in a supported format (e.g., WAV, MP3) and should not exceed the size limit of 50 MB.
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
        Guid sessionId, IFormFile audio, CancellationToken cancellationToken)
    {
        var result = await _recordingService.CompleteAsync(sessionId, CurrentUserId, audio, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Retries the transcription of a recording session. This is useful if the initial transcription failed or if the user has updated the district/street information and wants to reprocess the audio with the new context.
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
    /// Lightweight status check — meant for frequent polling right after upload/retry.
    /// </summary>
    [HttpGet("{sessionId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<RecordingSessionStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _recordingService.GetStatusAsync(sessionId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Matched plates only — call once after status becomes Completed, to show a one-time
    /// result popup instead of polling the full plates list.
    /// </summary>
    [HttpGet("{sessionId:guid}/matched")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RecordedPlateRowDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatched(
        Guid sessionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _recordingService.GetMatchedPlatesAsync(sessionId, CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Gets all recording sessions for the current user. This returns a list of all sessions, including their IDs, display names, statuses, durations, and counts of GPS points and plates.
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
    /// Gets a specific recording session by its ID. This returns detailed information about the session, including its district, street, status, processing errors, and counts of GPS points and plates.
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
    /// Gets the recorded plates for a specific recording session. This returns a paginated list of plates that were recorded during the session, along with their details such as normalized plate number, notes, vehicle type, street, district, recorded timestamp, location link, and match status.
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
    /// Gets the audio file for a specific recording session. This allows the user to download the original audio recording associated with the session. If the session does not exist or the audio file is not available, a 404 Not Found response is returned.
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
    /// Exports the recorded plates for a specific recording session to an Excel file. This allows the user to download the data in a structured format for further analysis or reporting. If the session does not exist or there are no recorded plates, a 404 Not Found response is returned.
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
    /// Appends data to an existing recording session's data file. This is useful for adding additional information or corrections to the session after it has been created. The request should include the ID of the data file to append to and the mode of appending (e.g., overwrite, append). The response will indicate whether the operation was successful.
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
    /// Deletes a specific recording session. This operation removes the session and all associated data, including recorded plates and audio files. If the session does not exist or cannot be deleted due to constraints, an appropriate error response is returned.
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
    /// Deletes all recording sessions for the current user. This operation removes all sessions and their associated data, including recorded plates and audio files. Use this operation with caution, as it cannot be undone.
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