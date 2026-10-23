using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Bank.Shared.DTOs.AuthDTOs
{
    public class LoginDTO
    {
        [EmailAddress]
        public required string Email { get; set; }
        public required string Password { get; set; }

        [Required, StringLength(128, MinimumLength = 16)]
        public required string DeviceId { get; set; }

        [StringLength(150)]
        public string? DeviceName { get; set; }
    }

    public class LoginResponseDTO
    {
        public string AccessToken { get; set; } = null!;
        public DateTime ExpireAt { get; set; }
        public string RefreshToken { get; set; } = null!;
        public DateTime RefreshTokenExpireAt { get; set; }
        public UserLoginDTO UserLogin { get; set; } = null!;
    }

    public class RefreshTokenRequestDTO
    {
        [Required] public required string RefreshToken { get; set; }

        [Required, StringLength(128, MinimumLength = 16)]
        public required string DeviceId { get; set; }
    }

    public class LogoutRequestDTO
    {
        [Required] public required string RefreshToken { get; set; }
    }
    public class UserLoginDTO
    {
     public string Id { get; set; }
     public string FullName { get; set; }
     public string Role { get; set; }

    }
}

