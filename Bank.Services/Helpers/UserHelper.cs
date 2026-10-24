using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace Bank.Services.Helpers
{
    public static class UserHelper
    {
        public static string? GetCurrentUserId(IHttpContextAccessor _contextAccessor)
        {
            return _contextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
