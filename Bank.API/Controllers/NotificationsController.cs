using Bank.Shared.DTOs.SubscriptionDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Bank.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly IDeviceTokenService _service;
        public NotificationsController(IDeviceTokenService service) => _service = service;

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        /// <summary>Registers or refreshes the device's FCM token. Call after login and on token refresh.</summary>
        [HttpPost("device-token")]
        public async Task<IActionResult> Register([FromBody] RegisterDeviceTokenDto dto, CancellationToken ct)
        {
            var result = await _service.RegisterAsync(CurrentUserId, dto, ct);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>Removes the device's FCM token (e.g. when the user turns notifications off).</summary>
        [HttpPost("device-token/remove")]
        public async Task<IActionResult> Remove([FromBody] RemoveDeviceTokenDto dto, CancellationToken ct)
        {
            var result = await _service.RemoveAsync(CurrentUserId, dto.Token, ct);
            return StatusCode(result.StatusCode, result);
        }
    }
}
