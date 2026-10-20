
using Bank.DAL.Models.BaseModels;
using Bank.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        public JWTService(IConfiguration conf)
        {
            _conf = conf;
        }
        public Task<string> GenerateJWTToken(ApplicationUser user)
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

            // مفيش صلاحيات في الـ token — الـ API بيقراها من الداتابيز في كل طلب (IPermissionService)،
            // والفرونت بيجيبها من GET api/auth/me/permissions.

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

            return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        }
    }
}
