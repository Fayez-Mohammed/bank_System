using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.DAL.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedRolesAsync(IServiceProvider service)
        {
         using   var scope = service.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
         //   bool Studentexist = await roleManager.RoleExistsAsync(UserTypes.Student.ToString());

            foreach(var role in Enum.GetNames<UserRole>())
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }
        public static async Task AddAdminAsync(IServiceProvider service)
        {
            using var scope = service.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                FullName="Super Admin",
                Email = "admin@gmail.com",
                EmailConfirmed = true,
                UserName = "admin@gmail.com",
                Role = UserRole.SuperAdmin,

                CreatedAt = DateTime.UtcNow
            };
            var Exist =await userManager.FindByEmailAsync(user.Email);
            if (Exist != null)
                return;
            var result = await userManager.CreateAsync(user, "**AAaa010");
            if (!result.Succeeded)
            {
                var errors = string.Join(",", result.Errors.Select(e => e.Description));
                throw new Exception($"failed to seed admin errors: {errors} ");
            }
            await userManager.AddToRoleAsync(user, UserTypes.SuperAdmin.ToString());

        }
    }
}

