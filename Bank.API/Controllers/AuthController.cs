using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AdminDTOs;
using Bank.Shared.DTOs.AuthDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _service;
        private readonly IPermissionService _permissions;
        public AuthController(IAuthService service, IPermissionService permissions)
        {
            _service = service;
            _permissions = permissions;
        }
        ///// <summary>
        ///// Registers a new user with the provided registration details.
        ///// </summary>
        ///// <param name="registerDTO"></param>
        ///// <returns></returns>
        //[HttpPost("register")]
        //public async Task<IActionResult> Register([FromBody] RegisterDTO registerDTO)
        //{
        //    var result = await _service.RegisterAsync(registerDTO);
        //    return Created("", result);
        //}
       // ==========================
        /// <summary>
        /// Authenticates a user with the provided login credentials and returns a JWT token if successful.
        /// </summary>
        /// <param name="loginDTO"></param>
        /// <returns></returns>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDTO loginDTO)
        {
            var result=await _service.LoginAsync(loginDTO);
            return Ok(result);
        }

        // ==========================
        /// <summary>
        /// Returns the current user's permissions, read from the database (they are not in the JWT),
        /// so the frontend can show or hide actions. Call it after login and whenever a 403 comes back.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet("me/permissions")]
        [Authorize]
        [ProducesResponseType(typeof(ApiResponse<MyPermissionsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMyPermissions(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var permissions = await _permissions.GetMyPermissionsAsync(userId, cancellationToken);
            return permissions is null
                ? NotFound(ApiResponse<object>.Fail("المستخدم غير موجود.", null, 404))
                : Ok(ApiResponse<MyPermissionsDto>.SuccessResponse(permissions));
        }

    }
}

