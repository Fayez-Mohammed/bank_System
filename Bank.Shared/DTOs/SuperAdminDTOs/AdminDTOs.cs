using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Bank.Shared.DTOs.SuperAdminDTOs
{
    public class CreateAdminDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = null!;

        //[Required, MaxLength(100)]
        //public string Username { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required, MinLength(8)]
        public string Password { get; set; } = null!;
    }

    public class UpdateAdminDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!;
    }

    public class AdminResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = null!;
       // public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        // The group this admin currently administers, if any (Group.AdminId == this.Id)
        public Guid? AdministeredGroupId { get; set; }
        public string? AdministeredGroupName { get; set; }
    }

}
