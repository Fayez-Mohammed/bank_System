using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.ReferralJobDTOs;
using CsvHelper.Configuration.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/referral-jobs")]
[Authorize]
public class ReferralJobsController : ControllerBase
{
    private readonly IReferralJobService _referralJobService;
    private readonly ILogger<ReferralJobsController> _logger;

    public ReferralJobsController(IReferralJobService referralJobService, ILogger<ReferralJobsController> logger)
    {
        _referralJobService = referralJobService;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    /// <summary>
    /// Inspects a referral job file upload and returns the inspection results.
    /// </summary>
    /// <param name="file"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("upload/inspect")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> InspectUpload(IFormFile file, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.InspectUploadAsync(file, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Uploads a referral job file and creates a new referral job.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload([FromForm] CreateReferralJobUploadRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.CreateFromUploadAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Uploads a referral job from pasted text and creates a new referral job.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("paste")]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Paste([FromBody] CreateReferralJobPasteRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.CreateFromPasteAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Gets the status of a referral job by its ID.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{referralJobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReferralJobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid referralJobId, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.GetStatusAsync(referralJobId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Gets the matched results of a referral job by its ID, with pagination support.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{referralJobId:guid}/matched")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MatchResultRowDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatched(
        Guid referralJobId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _referralJobService.GetMatchedResultsAsync(referralJobId, CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Gets the unmatched results of a referral job by its ID, with pagination support.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{referralJobId:guid}/unmatched")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUnmatched(
        Guid referralJobId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _referralJobService.GetUnmatchedResultsAsync(referralJobId, CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Appends unmatched results of a referral job to a check file.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpPost("{referralJobId:guid}/append-unmatched-to-check-file")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AppendUnmatchedToCheckFile(
        Guid referralJobId, [FromBody] AppendUnmatchedToCheckFileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.AppendUnmatchedToCheckFileAsync(referralJobId, CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    ///// <summary>
    ///// Gets a WhatsApp share link for a referral job by its ID.
    ///// </summary>
    ///// <param name="referralJobId"></param>
    ///// <param name="cancellationToken"></param>
    ///// <returns></returns>
    //[HttpGet("{referralJobId:guid}/share/whatsapp")]
    //[ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    //[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    //public async Task<IActionResult> GetWhatsAppShareLink(Guid referralJobId, CancellationToken cancellationToken)
    //{
    //    var result = await _referralJobService.GetWhatsAppShareLinkAsync(referralJobId, CurrentUserId, cancellationToken);
    //    return StatusCode(result.StatusCode, result);
    //}


    /// <summary>
    /// Gets all referral jobs for the current user, with pagination support.
    /// </summary>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReferralJobSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _referralJobService.GetReferralJobsAsync(CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Deletes a referral job by its ID.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpDelete("{referralJobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid referralJobId, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.DeleteAsync(referralJobId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Exports the matched results of a referral job to an Excel file.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{referralJobId:guid}/matched/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportMatched(Guid referralJobId, CancellationToken cancellationToken)
    {
        try
        {
            var (bytes, fileName) = await _referralJobService.ExportMatchedAsync(referralJobId, CurrentUserId, cancellationToken);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
            return StatusCode(response.StatusCode, response);
        }
    }
    /// <summary>
    ///     Exports the unmatched results of a referral job to an Excel file.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{referralJobId:guid}/unmatched/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportUnmatched(Guid referralJobId, CancellationToken cancellationToken)
    {
        try
        {
            var (bytes, fileName) = await _referralJobService.ExportUnmatchedAsync(referralJobId, CurrentUserId, cancellationToken);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
            return StatusCode(response.StatusCode, response);
        }
    }
    /// <summary>
    ///   Exports the results of a referral job to an Excel file.
    /// </summary>
    /// <param name="referralJobId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("{referralJobId:guid}/export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
    Guid referralJobId,
    CancellationToken cancellationToken)
    {
        var result = await _referralJobService.ExportResultsAsync(
            referralJobId,
            CurrentUserId,
            cancellationToken);

        return File(
            result.FileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.FileName);
    }
}