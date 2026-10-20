
using Bank.DAL.Context;
using Bank.DAL.Models.BaseModels;
using Bank.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Bank.Services.Implementations
{
    public class JWTService : IJWTService
    {
        private readonly IConfiguration  _conf;
        private readonly AppDbContext _dbContext;
        public JWTService(IConfiguration conf, AppDbContext dbContext)
        {
            _conf = conf;
            _dbContext = dbContext;
        }
        public async Task<string> GenerateJWTToken(ApplicationUser user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_conf["Auth:JWT:Key"] ?? "this is my secret key it must be very secure and hidden"));
            var crds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var roleString = user.Role.ToString();

            var clims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.FullName ?? string.Empty),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Role, roleString),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())
    };

            //if (roleString is "Admin" or "SuperAdmin")
            //{
            //    clims.Add(new Claim("CanDownload", "true"));
            //    clims.Add(new Claim("CanToggleFiles", "true"));
            //    clims.Add(new Claim("CanAppendToTeamFiles", "true"));
            //    clims.Add(new Claim("CanFilter", "true"));

            //}
            //else
            //{
            if (roleString is "User") { 
                var permissions = await _dbContext.UserPermissions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == user.Id);

                if (permissions != null)
                {
                    // للعرض في الفرونت بس — الـ API بيتحقق من الداتابيز في كل طلب (IPermissionService)
                    if (permissions.CanManageCheckFiles)
                        clims.Add(new Claim("CanManageCheckFiles", "true"));

                    if (permissions.CanUploadDataFiles)
                        clims.Add(new Claim("CanUploadDataFiles", "true"));

                    if (permissions.CanSort)
                        clims.Add(new Claim("CanSort", "true"));
                }
            }

            if (!int.TryParse(_conf["Auth:JWT:DurationInMinuts"], out int minuts))
            {
                minuts = 60;
            }

            var token = new JwtSecurityToken(
                signingCredentials: crds,
                issuer: _conf["Auth:JWT:Issuer"],
                audience: _conf["Auth:JWT:Audience"],
                expires: DateTime.UtcNow.AddMinutes(minuts),
                claims: clims
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
