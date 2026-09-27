using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.DataFileDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Bank.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class HistoricalPlatesController : ControllerBase
    {
        private readonly IDataFileService _dataFileService;
        private readonly ILogger<HistoricalPlatesController> _logger;

        public HistoricalPlatesController(IDataFileService dataFileService, ILogger<HistoricalPlatesController> logger)
        {
            _dataFileService = dataFileService;
            _logger = logger;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        /// <summary>
        /// Updates a single historical-data row (the 8 fixed columns coming from the data file).
        /// This is a real update on historical_plates; any match_results already linked to this
        /// row are synced (Found* fields) but no new matching is performed.
        /// </summary>
        [HttpPatch("{historicalPlateId:long}")]
        [Authorize(Policy = "CanUploadDataFiles")]
        [ProducesResponseType(typeof(ApiResponse<UpdateHistoricalPlateResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(
            long historicalPlateId, [FromBody] UpdateHistoricalPlateDto request, CancellationToken cancellationToken)
        {
            var result = await _dataFileService.UpdateHistoricalPlateAsync(historicalPlateId, CurrentUserId, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
    }
}