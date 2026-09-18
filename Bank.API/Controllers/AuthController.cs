using Bank.Services.Interfaces;
using Bank.Shared.DTOs.AuthDTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _service;
        public AuthController(IAuthService service)
        {
            _service = service;
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

    }
}

