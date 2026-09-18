using Bank.DAL.Models.BaseModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Interfaces
{
    public interface IJWTService
    {
        Task<string> GenerateJWTToken(ApplicationUser user);
    }
}
