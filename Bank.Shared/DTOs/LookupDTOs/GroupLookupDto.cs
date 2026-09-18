using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Shared.DTOs.LookupDTOs
{
    public class GroupLookupDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
    }
    public class UserLookupDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
    }
    
   
}
