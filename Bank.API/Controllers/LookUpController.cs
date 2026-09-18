using Bank.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "SuperAdmin")]
    public class LookUpController : ControllerBase
    {
        private readonly ILookupService _lookupService;
        public LookUpController(ILookupService lookupService)
        {
            _lookupService = lookupService;
        }
        /// <summary>
        /// Get a list of groups based on the search term.
        /// </summary>
        /// <param name="searchTerm"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet("groups")]
        public async Task<IActionResult> GetGroups(string? searchTerm, CancellationToken cancellationToken)
        {
            var response = await _lookupService.GetGroupsAsync(searchTerm, cancellationToken);
            return Ok(response);
        }
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(string? searchTerm, CancellationToken cancellationToken)
        {
            var response = await _lookupService.GetUsersAsync(searchTerm, cancellationToken);
            return Ok(response);
        }
        [HttpGet("admins")]
        public async Task<IActionResult> GetAdmins(string? searchTerm, CancellationToken cancellationToken)
        {
            var response = await _lookupService.GetAdminsAsync(searchTerm, cancellationToken);
            return Ok(response);
        }
    }
}
