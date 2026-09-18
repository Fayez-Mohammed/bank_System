using Bank.Shared.DTOs;
using Bank.Shared.DTOs.LookupDTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Interfaces
{
    public interface ILookupService
    {
        public Task<ApiResponse<List<GroupLookupDto>>> GetGroupsAsync(string? searchTerm, CancellationToken cancellationToken);
        public Task<ApiResponse<List<UserLookupDto>>> GetUsersAsync(string? searchTerm, CancellationToken cancellationToken);
        public Task<ApiResponse<List<UserLookupDto>>> GetAdminsAsync(string? searchTerm, CancellationToken cancellationToken);
    }
}
