using System.Security.Cryptography;
using System.Text;
using Bank.Services.Implementations;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.TrainingDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

// Read by the AI server, which has no user account: protected by a shared key (header X-Training-Key,
// setting Training:ApiKey). With no key configured the endpoint stays closed.
[ApiController]
[Route("api/training")]
[AllowAnonymous]
public class TrainingController : ControllerBase
{
    private const string KeyHeader = "X-Training-Key";

    private readonly ITrainingCorrectionsService _service;
    private readonly IConfiguration _configuration;

    public TrainingController(ITrainingCorrectionsService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    /// <summary>User changes to plates, oldest first. Pass the last id you received as afterId.</summary>
    [HttpGet("corrections")]
    [ProducesResponseType(typeof(ApiResponse<PlateCorrectionFeedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCorrections(
        [FromQuery] long afterId = 0, [FromQuery] int limit = 500, CancellationToken cancellationToken = default)
    {
        var expected = _configuration["Training:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        if (!KeyMatches(Request.Headers[KeyHeader].ToString(), expected))
            return Unauthorized();

        var feed = await _service.GetAfterAsync(afterId, limit, cancellationToken);
        return Ok(ApiResponse<PlateCorrectionFeedDto>.SuccessResponse(feed));
    }

    private static bool KeyMatches(string given, string expected)
        => CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
