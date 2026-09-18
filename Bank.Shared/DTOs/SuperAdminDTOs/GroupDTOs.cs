using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Bank.Shared.DTOs.SuperAdminDTOs
{
    public class CreateGroupDto
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = null!;

        [Required]
        public Guid AdminId { get; set; }
    }

    public class UpdateGroupDto
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = null!;
    }

    public class AssignAdminToGroupDto
    {
        [Required]
        public Guid AdminId { get; set; }
    }

    public class GroupResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
        public Guid? AdminId { get; set; }
        public string AdminName { get; set; } = null!;
        public int MembersCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
