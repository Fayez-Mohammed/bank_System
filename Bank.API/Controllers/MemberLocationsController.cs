using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.MemberLocationDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

[ApiController]
[Route("api/member-locations")]
[Authorize]
public class MemberLocationsController : ControllerBase
{
    private readonly IMemberLocationService _memberLocationService;

    public MemberLocationsController(IMemberLocationService memberLocationService)
    {
        _memberLocationService = memberLocationService;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// زرار "مكان الأعضاء" لأدمن الجروب: بيطلب مكان كل عضو في جروبه في اللحظة دي
    /// (مرة واحدة، مفيش تتبع). اللي مش متاح بيرجع isAvailable=false والإحداثيات null.
    /// </summary>
    [HttpPost("request")]
    [ProducesResponseType(typeof(ApiResponse<List<MemberLocationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestLocations(CancellationToken cancellationToken)
    {
        var result = await _memberLocationService.RequestGroupLocationsAsync(CurrentUserId, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
