using Bank.DAL.Models.BaseModels;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AuthDTOs;
using Bank.Shared.Enums;
using Bank.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using SendGrid.Helpers.Errors.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Implementations
{
    public class AuthService : IAuthService
    {

        private readonly IUserService _userService;
        private readonly IJWTService _jWTService;
        private readonly IConfiguration _conf;
        public AuthService(IUserService userService, IJWTService jWTService, IConfiguration conf)
        {
            _userService = userService;
            _jWTService = jWTService;
            _conf = conf;
        }
        public async Task<ApiResponse<RegisterResponseDTO>> RegisterAsync(RegisterDTO registerDTO)
        {
            if (registerDTO is null)
                return ApiResponse<RegisterResponseDTO>.Fail("please fill the form");
            var existingUser = await _userService.GetByEmailAsync(registerDTO.Email);
            if (existingUser is not null)
                return ApiResponse<RegisterResponseDTO>.Fail("Email Already Exist");
            ApplicationUser user = new ApplicationUser
            {
                FullName = registerDTO.FullName,
                Email = registerDTO.Email,
                UserName = registerDTO.Email,
                Role = UserRole.Admin

            };
            var Appuser = await _userService.CreateUserAsync(user, registerDTO.Password);
            var res = new RegisterResponseDTO
            {
                UserId = Appuser.Id.ToString(),
                Email = Appuser.Email
            };
            return ApiResponse<RegisterResponseDTO>.SuccessResponse(res, "User registered successfully", 201);
        }

        public async Task<ApiResponse<LoginResponseDTO>> LoginAsync(LoginDTO loginDTO)
        {
            if (loginDTO is null)
                return ApiResponse<LoginResponseDTO>.Fail("please fill the form");
            var user = await _userService.GetByEmailAsync(loginDTO.Email);
            if (user is null)
                return ApiResponse<LoginResponseDTO>.Fail("Email or password is not correct");
            bool isCorrect = await _userService.CheckPasswordAsync(user, loginDTO.Password);
            if (!isCorrect)
                return ApiResponse<LoginResponseDTO>.Fail("Email or password is not correct");
            string token = await _jWTService.GenerateJWTToken(user);

            if (!int.TryParse(_conf["Auth:JWT:DurationInMinuts"], out int minuts))
            {
                minuts = 60;
            }
            var result = new LoginResponseDTO
            {
                AccessToken = token,
                ExpireAt = DateTime.UtcNow.AddMinutes(minuts),
                UserLogin = new UserLoginDTO
                {
                    Id = user.Id.ToString(),
                    FullName = user.FullName,
                    Role = user.Role.ToString()
                }

            };
            return ApiResponse<LoginResponseDTO>.SuccessResponse(result, "User logged in successfully", 200);

        }
    }
}

