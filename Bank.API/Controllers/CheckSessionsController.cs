using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.LiveCheckDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

// ⭐ الجزء الـ REST بس (تاريخ، نتائج، تصدير، حذف، تشيك يدوي) — الجزء اللحظي
// نفسه شغال عن طريق LiveCheckHub (SignalR) مش من هنا.
[ApiController]
[Route("api/check-sessions")]
[Authorize]
public class CheckSessionsController : ControllerBase
{
    private readonly ILiveCheckSessionService _service;

    public CheckSessionsController(ILiveCheckSessionService service)
    {
        _service = service;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    /// <summary>تعديل صف لوحة في تشييك لحظي (مع خيار تطبيق الحي/الشارع على الجلسة كلها).</summary>
    [HttpPut("plates/{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 409)]
    public async Task<IActionResult> UpdateRow(long id, [FromBody] UpdateLiveCheckLogRowDto request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateRowAsync(id, CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>حذف صف لوحة من تشييك لحظي.</summary>
    [HttpDelete("plates/{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 409)]
    public async Task<IActionResult> DeleteRow(long id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteRowAsync(id, CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>تشيك يدوي — standalone، بيعمل ميني-session لوحده وبيرجّع النتيجة فورًا.</summary>
    [HttpPost("manual-check")]
    [ProducesResponseType(typeof(ApiResponse<ManualLiveCheckResultDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> CreateManualCheck([FromBody] CreateManualLiveCheckRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateManualCheckAsync(CurrentUserId, request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<CheckSessionSummaryDto>>), 200)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(CurrentUserId, cancellationToken);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpGet("{sessionId:guid}/plates")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<LiveCheckLogRowDto>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetPlates(
        Guid sessionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetPlatesAsync(sessionId, CurrentUserId, page, pageSize, cancellationToken);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpGet("{sessionId:guid}/export")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Export(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var (bytes, fileName) = await _service.ExportAsync(sessionId, CurrentUserId, cancellationToken);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }
    public record ExportSessionsRequest(List<Guid> SessionIds);

    [HttpPost("multi-export")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Export([FromBody] ExportSessionsRequest request, CancellationToken cancellationToken)
    {
        if (request?.SessionIds is null || request.SessionIds.Count == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("يجب تحديد جلسة واحدة على الأقل للتصدير."));
        }

        try
        {
            var (bytes, fileName) = await _service.ExportAsync(request.SessionIds, CurrentUserId, cancellationToken);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }
    /// <summary>
    /// كل لوحات التشييك لكل الجلسات (مش جلسة معينة)، الأحدث الأول، مع paging.
    /// </summary>
    [HttpGet("plates")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<LiveCheckLogRowDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPlates(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetAllPlatesAsync(CurrentUserId, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Export كل لوحات التشييك لكل الجلسات في ملف Excel واحد.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAll(CancellationToken cancellationToken)
    {
        var (bytes, fileName) = await _service.ExportAllAsync(CurrentUserId, cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
    /// <summary>
    /// بيحوّل كل لوحات التشييك اللي لسه ما اتحوّلتش لملف داتا — append على ملف داتا موجود
    /// (ExistingDataFileId) أو ملف داتا جديد (NewDataFileName). التشييك نفسه مش بيتمسح،
    /// والمرة الجاية بياخد الجديد بس.
    /// </summary>
    [HttpPost("convert-to-data-file")]
    [ProducesResponseType(typeof(ApiResponse<ConvertCheckLogsToDataFileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConvertToDataFile(
        [FromBody] ConvertCheckLogsToDataFileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.ConvertToDataFileAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Delete(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(sessionId, CurrentUserId, cancellationToken);
        if (result.Success) return Ok(result);
        return result.StatusCode == 409 ? Conflict(result) : NotFound(result);
    }

    /// <summary>"مسح كل التسجيلات" — حذف نهائي لكل جلسات التشييك في الـ scope.</summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<int>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAllAsync(CurrentUserId, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
