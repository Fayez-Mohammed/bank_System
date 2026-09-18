using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Bank.Shared.DTOs.AdminDTOs
{
    public class UpdateGroupMemberDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!;
    }

    public class GroupMemberResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public UserPermissionDto Permissions { get; set; } = new();
    }
}
