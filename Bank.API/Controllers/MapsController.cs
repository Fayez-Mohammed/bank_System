using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.MapDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/maps")]
[Authorize]
public class MapsController : ControllerBase
{
    private readonly IMapService _mapService;
    private readonly ILogger<MapsController> _logger;

    public MapsController(IMapService mapService, ILogger<MapsController> logger)
    {
        _mapService = mapService;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// زر "الأحدث" — نتيجة أحدث عملية فرز مكتملة في مساحة عمل المستخدم.
    /// </summary>
    [HttpGet("latest")]
    [ProducesResponseType(typeof(ApiResponse<MapResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatest(CancellationToken cancellationToken)
    {
        var result = await _mapService.GetLatestAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// زر "الملف المرفوع" — نتيجة آخر ملف Excel اترفع لمساحة عمل المستخدم.
    /// متاح لكل أعضاء الجروب للعرض.
    /// </summary>
    [HttpGet("uploaded-file")]
    [ProducesResponseType(typeof(ApiResponse<MapUploadedFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUploadedFile(CancellationToken cancellationToken)
    {
        var result = await _mapService.GetUploadedFileAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// رفع/استبدال ملف الخريطة — أدمن الجروب بس (أو اليوزر المستقل نفسه).
    /// </summary>
    [HttpPost("uploaded-file")]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(typeof(ApiResponse<MapUploadedFileDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UploadFile([FromForm] UploadMapFileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _mapService.UploadFileAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// حذف ملف الخريطة المرفوع — أدمن الجروب بس (أو اليوزر المستقل نفسه).
    /// </summary>
    [HttpDelete("uploaded-file")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUploadedFile(CancellationToken cancellationToken)
    {
        var result = await _mapService.DeleteUploadedFileAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
