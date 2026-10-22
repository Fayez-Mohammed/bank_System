using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Shared.DTOs.UserDTOs
{
    public class MyGroupDTO
    {
        public bool IsInGroup { get; set; }
        public string? UserRole { get; set; }
        public string? GroupId { get; set; }
        public string? GroupName { get; set; }

    }
}
