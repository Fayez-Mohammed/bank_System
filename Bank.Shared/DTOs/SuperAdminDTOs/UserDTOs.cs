using Bank.Shared.DTOs.AdminDTOs;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Bank.Shared.DTOs.SuperAdminDTOs
{
    public class CreateUserDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = null!;

        //[Required, MaxLength(100)]
        //public string Username { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required, MinLength(8)]
        public string Password { get; set; } = null!;

        // Optional: create the user already assigned to a group.
        public Guid? GroupId { get; set; }
        public UserPermissionDto? Permissions { get; set; }
    }

    public class UpdateUserDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!;
    }

    public class AssignUserToGroupDto
    {
        [Required]
        public Guid GroupId { get; set; }
    }

    public class UserResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = null!;
        //public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? GroupId { get; set; }
        public string? GroupName { get; set; }
        public UserPermissionDto Permissions { get; set; } = new();

    }
}
