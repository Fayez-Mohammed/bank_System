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
    }
    public class LoginResponseDTO
    {
        public string AccessToken { get; set; }
        public DateTime ExpireAt {  get; set; }
        public UserLoginDTO UserLogin { get; set; }
       
    }
    public class UserLoginDTO
    {
     public string Id { get; set; }
     public string FullName { get; set; }
     public string Role { get; set; }

    }
}

