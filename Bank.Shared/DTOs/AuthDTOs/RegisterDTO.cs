using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Shared.DTOs.AuthDTOs
{
    public class RegisterDTO
    {
        public string FullName { get; set; }
        public  string Email { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
    public class RegisterResponseDTO
    {
        public string UserId { get; set; }
        public string Email { get; set; }
    }
}

