using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.DataFileDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers
{
    [ApiController]
    [Route("api/data-files")]
    [Authorize]
    public class DataFilesController : ControllerBase
    {
        private readonly IDataFileService _dataFileService;
        private readonly ILogger<DataFilesController> _logger;

        public DataFilesController(IDataFileService dataFileService, ILogger<DataFilesController> logger)
        {
            _dataFileService = dataFileService;
            _logger = logger;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        /// <summary>
        /// Gets all data files for the current user.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<DataFilesListResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<DataFilesListResponseDto>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            var result = await _dataFileService.GetDataFilesAsync(CurrentUserId, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        /// <summary>
        /// Creates a new data file for the current user.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPost]
        [RequestSizeLimit(200_000_000)]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromForm] CreateDataFileRequestDto request, CancellationToken cancellationToken)
        {
            var result = await _dataFileService.CreateDataFileAsync(CurrentUserId, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        /// <summary>
        /// Appends data to an existing data file for the current user.
        /// </summary>
        /// <param name="dataFileId"></param>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPost("{dataFileId:guid}/append")]
        [Authorize(Policy = "CanAppendToTeamFiles")]
        [RequestSizeLimit(200_000_000)]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Append(Guid dataFileId, [FromForm] AppendDataFileRequestDto request, CancellationToken cancellationToken)
        {
            var result = await _dataFileService.AppendAsync(dataFileId, CurrentUserId, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        /// <summary>
        /// Toggles the active status of a data file for the current user.
        /// </summary>
        /// <param name="dataFileId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPatch("{dataFileId:guid}/toggle-active")]
       // [Authorize(Policy = "CanToggleFiles")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ToggleActive(Guid dataFileId, CancellationToken cancellationToken)
        {
            var result = await _dataFileService.ToggleActiveAsync(dataFileId, CurrentUserId, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        /// <summary>
        /// Reorders data files for the current user.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPatch("reorder")]
        [Authorize(Policy = "CanToggleFiles")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Reorder([FromBody] ReorderDataFilesRequestDto request, CancellationToken cancellationToken)
        {
            var result = await _dataFileService.ReorderAsync(CurrentUserId, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        /// <summary>
        /// Deletes a data file for the current user.
        /// </summary>
        /// <param name="dataFileId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpDelete("{dataFileId:guid}")]
        [Authorize]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid dataFileId, CancellationToken cancellationToken)
        {
            var result = await _dataFileService.DeleteAsync(dataFileId, CurrentUserId, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        /// <summary>
        /// Downloads a data file for the current user.
        /// </summary>
        /// <param name="dataFileId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet("{dataFileId:guid}/download")]
        [Authorize(Policy = "CanDownload")]
        [Produces("application/octet-stream", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Download(Guid dataFileId, CancellationToken cancellationToken)
        {
            try
            {
                var (stream, contentType, fileName) = await _dataFileService.DownloadAsync(dataFileId, CurrentUserId, cancellationToken);
                return File(stream, contentType, fileName);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Download failed for DataFile {DataFileId}", dataFileId);
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Download unauthorized for DataFile {DataFileId}", dataFileId);
                return Forbid();
            }
        }
        /// <summary>
        /// Gets the import status of a data file for the current user.
        /// </summary>
        /// <param name="dataFileId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet("{dataFileId:guid}/import-status")]
        public async Task<IActionResult> GetImportStatus(Guid dataFileId, CancellationToken cancellationToken)
        {
            var result = await _dataFileService.GetImportStatusAsync(dataFileId, CurrentUserId, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
    }
}