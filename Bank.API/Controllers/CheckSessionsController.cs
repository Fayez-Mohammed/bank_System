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

    /// <summary>تشيك يدوي — standalone، بيعمل ميني-session لوحده وبيرجّع النتيجة فورًا.</summary>
    [HttpPost("manual-check")]
    public async Task<IActionResult> CreateManualCheck([FromBody] CreateManualLiveCheckRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateManualCheckAsync(CurrentUserId, request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(CurrentUserId, cancellationToken);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpGet("{sessionId:guid}/plates")]
    public async Task<IActionResult> GetPlates(
        Guid sessionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetPlatesAsync(sessionId, CurrentUserId, page, pageSize, cancellationToken);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpGet("{sessionId:guid}/export")]
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

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Delete(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(sessionId, CurrentUserId, cancellationToken);
        if (result.Success) return Ok(result);
        return result.StatusCode == 409 ? Conflict(result) : NotFound(result);
    }

    /// <summary>"مسح كل التسجيلات" — حذف نهائي لكل جلسات التشييك في الـ scope.</summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAllAsync(CurrentUserId, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
