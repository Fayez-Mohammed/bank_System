using Bank.API.Authorization;
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
    /// Treats the user's active check file as a referral file and runs it through the normal
    /// referral matching flow against all active historical data files. Results show BankName
    /// and VehicleType from the check file as the "referral columns", alongside the usual
    /// historical data columns.
    /// </summary>
    [HttpPost("from-check-file")]
    [Authorize(Policy = PermissionPolicies.Sort)]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFromCheckFile(CancellationToken cancellationToken)
    {
        var result = await _referralJobService.CreateFromActiveCheckFileAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
    /// <summary>
    /// Exports a single combined Excel file from multiple referral jobs' matched results,
    /// grouped by data file across all of them (so e.g. "مكة 2026" rows from any selected
    /// referral job end up in the same group). Meant to be picked up by the client and shared
    /// via the device's native share sheet (e.g. to WhatsApp).
    /// </summary>
    [HttpPost("export-combined")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportCombined(
        [FromBody] ExportCombinedReferralResultsRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var (bytes, fileName) = await _referralJobService.ExportCombinedResultsAsync(
                request.ReferralJobIds, CurrentUserId, cancellationToken);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (ArgumentException ex)
        {
            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status400BadRequest);
            return StatusCode(response.StatusCode, response);
        }
        catch (KeyNotFoundException ex)
        {
            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
            return StatusCode(response.StatusCode, response);
        }
    }
    /// <summary>
    /// Inspects a referral job file upload: returns each sheet's headers, row count,
    /// and suggested plate/color/vehicle-type/model columns (suggestions only — not final).
    /// </summary>
    [HttpPost("upload/inspect")]
    [Authorize(Policy = PermissionPolicies.Sort)]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> InspectUpload(IFormFile file, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.InspectUploadAsync(file, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Uploads a referral job file and creates a new referral job. The client must send,
    /// per selected sheet, the confirmed plate column index, plus the referral/historical
    /// columns chosen for display (defaults to inspect's suggestions if unchanged).
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Policy = PermissionPolicies.Sort)]
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
    [HttpPost("paste")]
    [Authorize(Policy = PermissionPolicies.Sort)]
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
    [HttpGet("{referralJobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReferralJobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid referralJobId, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.GetStatusAsync(referralJobId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gets the matched results of a referral job, grouped by data file and colored by
    /// duplicate plate, using the historical/referral columns selected on the job.
    /// </summary>
    [HttpGet("{referralJobId:guid}/results")]
    [ProducesResponseType(typeof(ApiResponse<ReferralResultsResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResults(Guid referralJobId, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.GetResultsAsync(referralJobId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gets the unmatched results of a referral job by its ID, with pagination support.
    /// </summary>
    [HttpGet("{referralJobId:guid}/unmatched")]
    [ProducesResponseType(typeof(ApiResponse<PagedResultDto<UnmatchedPlateRowDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUnmatched(
        Guid referralJobId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _referralJobService.GetUnmatchedResultsAsync(referralJobId, CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Deletes a matched plate — either from this referral job's results only
    /// (deleteFromDataFile=false, default), or permanently from the historical data
    /// (deleteFromDataFile=true), which also removes it from every other referral
    /// job's results and decrements the data file's row count.
    /// </summary>
    [HttpDelete("{referralJobId:guid}/results/{matchResultId:long}")]
    [ProducesResponseType(typeof(ApiResponse<DeleteMatchedPlateResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMatchedPlate(
        Guid referralJobId, long matchResultId, [FromQuery] bool deleteFromDataFile = false, CancellationToken cancellationToken = default)
    {
        var result = await _referralJobService.DeleteMatchedPlateAsync(
            referralJobId, matchResultId, CurrentUserId, deleteFromDataFile, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Appends All results of a referral job to a check file.
    /// </summary>
    [HttpPost("{referralJobId:guid}/append-All-to-check-file")]
    [ProducesResponseType(typeof(ApiResponse<AppendUnmatchedResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AppendAllToCheckFile(
        Guid referralJobId, [FromBody] AppendUnmatchedToCheckFileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.AppendAllToCheckFileAsync(referralJobId, CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gets a WhatsApp share link for a referral job by its ID.
    /// </summary>
    [HttpGet("{referralJobId:guid}/share/whatsapp")]
    [ProducesResponseType(typeof(ApiResponse<WhatsAppShareLinkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWhatsAppShareLink(Guid referralJobId, CancellationToken cancellationToken)
    {
        var result = await _referralJobService.GetWhatsAppShareLinkAsync(referralJobId, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gets all referral jobs for the current user, with pagination support.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResultDto<ReferralJobSummaryDto>>), StatusCodes.Status200OK)]
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
    ///   Exports the full results (matched + unmatched) of a referral job to an Excel file.
    /// </summary>
    [HttpGet("{referralJobId:guid}/export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(Guid referralJobId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _referralJobService.ExportResultsAsync(referralJobId, CurrentUserId, cancellationToken);
            return File(
                result.FileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
            return StatusCode(response.StatusCode, response);
        }
    }
}


//public class ReferralJobsController : ControllerBase
//{
//    private readonly IReferralJobService _referralJobService;
//    private readonly ILogger<ReferralJobsController> _logger;

//    public ReferralJobsController(IReferralJobService referralJobService, ILogger<ReferralJobsController> logger)
//    {
//        _referralJobService = referralJobService;
//        _logger = logger;
//    }

//    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
//    /// <summary>
//    /// Inspects a referral job file upload and returns the inspection results.
//    /// </summary>
//    /// <param name="file"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpPost("upload/inspect")]
//    [RequestSizeLimit(50_000_000)]
//    public async Task<IActionResult> InspectUpload(IFormFile file, CancellationToken cancellationToken)
//    {
//        var result = await _referralJobService.InspectUploadAsync(file, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }

//    /// <summary>
//    /// Uploads a referral job file and creates a new referral job.
//    /// </summary>
//    /// <param name="request"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpPost("upload")]
//    [RequestSizeLimit(50_000_000)]
//    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status202Accepted)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
//    public async Task<IActionResult> Upload([FromForm] CreateReferralJobUploadRequestDto request, CancellationToken cancellationToken)
//    {
//        var result = await _referralJobService.CreateFromUploadAsync(CurrentUserId, request, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    /// <summary>
//    /// Uploads a referral job from pasted text and creates a new referral job.
//    /// </summary>
//    /// <param name="request"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpPost("paste")]
//    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status202Accepted)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
//    public async Task<IActionResult> Paste([FromBody] CreateReferralJobPasteRequestDto request, CancellationToken cancellationToken)
//    {
//        var result = await _referralJobService.CreateFromPasteAsync(CurrentUserId, request, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    /// <summary>
//    /// Gets the status of a referral job by its ID.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpGet("{referralJobId:guid}")]
//    [ProducesResponseType(typeof(ApiResponse<ReferralJobStatusDto>), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> GetStatus(Guid referralJobId, CancellationToken cancellationToken)
//    {
//        var result = await _referralJobService.GetStatusAsync(referralJobId, CurrentUserId, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    /// <summary>
//    /// Gets the matched results of a referral job by its ID, with pagination support.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="page"></param>
//    /// <param name="pageSize"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpGet("{referralJobId:guid}/matched")]
//    [ProducesResponseType(typeof(ApiResponse<PagedResult<MatchResultRowDto>>), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> GetMatched(
//        Guid referralJobId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
//    {
//        var result = await _referralJobService.GetMatchedResultsAsync(referralJobId, CurrentUserId, page, pageSize, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    /// <summary>
//    /// Gets the unmatched results of a referral job by its ID, with pagination support.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="page"></param>
//    /// <param name="pageSize"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpGet("{referralJobId:guid}/unmatched")]
//    [ProducesResponseType(typeof(ApiResponse<PagedResult<string>>), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> GetUnmatched(
//        Guid referralJobId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
//    {
//        var result = await _referralJobService.GetUnmatchedResultsAsync(referralJobId, CurrentUserId, page, pageSize, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    /// <summary>
//    /// Appends unmatched results of a referral job to a check file.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="request"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpPost("{referralJobId:guid}/append-unmatched-to-check-file")]
//    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> AppendUnmatchedToCheckFile(
//        Guid referralJobId, [FromBody] AppendUnmatchedToCheckFileRequestDto request, CancellationToken cancellationToken)
//    {
//        var result = await _referralJobService.AppendUnmatchedToCheckFileAsync(referralJobId, CurrentUserId, request, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    ///// <summary>
//    ///// Gets a WhatsApp share link for a referral job by its ID.
//    ///// </summary>
//    ///// <param name="referralJobId"></param>
//    ///// <param name="cancellationToken"></param>
//    ///// <returns></returns>
//    //[HttpGet("{referralJobId:guid}/share/whatsapp")]
//    //[ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
//    //[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    //public async Task<IActionResult> GetWhatsAppShareLink(Guid referralJobId, CancellationToken cancellationToken)
//    //{
//    //    var result = await _referralJobService.GetWhatsAppShareLinkAsync(referralJobId, CurrentUserId, cancellationToken);
//    //    return StatusCode(result.StatusCode, result);
//    //}


//    /// <summary>
//    /// Gets all referral jobs for the current user, with pagination support.
//    /// </summary>
//    /// <param name="page"></param>
//    /// <param name="pageSize"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpGet]
//    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReferralJobSummaryDto>>), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> GetAll(
//        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
//    {
//        var result = await _referralJobService.GetReferralJobsAsync(CurrentUserId, page, pageSize, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    /// <summary>
//    /// Deletes a referral job by its ID.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpDelete("{referralJobId:guid}")]
//    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> Delete(Guid referralJobId, CancellationToken cancellationToken)
//    {
//        var result = await _referralJobService.DeleteAsync(referralJobId, CurrentUserId, cancellationToken);
//        return StatusCode(result.StatusCode, result);
//    }
//    /// <summary>
//    /// Exports the matched results of a referral job to an Excel file.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpGet("{referralJobId:guid}/matched/export")]
//    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> ExportMatched(Guid referralJobId, CancellationToken cancellationToken)
//    {
//        try
//        {
//            var (bytes, fileName) = await _referralJobService.ExportMatchedAsync(referralJobId, CurrentUserId, cancellationToken);
//            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
//        }
//        catch (KeyNotFoundException ex)
//        {
//            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
//            return StatusCode(response.StatusCode, response);
//        }
//    }
//    /// <summary>
//    ///     Exports the unmatched results of a referral job to an Excel file.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpGet("{referralJobId:guid}/unmatched/export")]
//    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> ExportUnmatched(Guid referralJobId, CancellationToken cancellationToken)
//    {
//        try
//        {
//            var (bytes, fileName) = await _referralJobService.ExportUnmatchedAsync(referralJobId, CurrentUserId, cancellationToken);
//            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
//        }
//        catch (KeyNotFoundException ex)
//        {
//            var response = ApiResponse<object>.Fail(ex.Message, null, StatusCodes.Status404NotFound);
//            return StatusCode(response.StatusCode, response);
//        }
//    }
//    /// <summary>
//    ///   Exports the results of a referral job to an Excel file.
//    /// </summary>
//    /// <param name="referralJobId"></param>
//    /// <param name="cancellationToken"></param>
//    /// <returns></returns>
//    [HttpGet("{referralJobId:guid}/export")]
//    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
//    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
//    public async Task<IActionResult> Export(
//    Guid referralJobId,
//    CancellationToken cancellationToken)
//    {
//        var result = await _referralJobService.ExportResultsAsync(
//            referralJobId,
//            CurrentUserId,
//            cancellationToken);

//        return File(
//            result.FileBytes,
//            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
//            result.FileName);
//    }
//}