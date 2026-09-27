using Bank.API.Filters;
using Bank.API.Hubs;
using Bank.API.MiddleWare;
using Bank.API.Services;
using Bank.DAL.Context;
using Bank.DAL.Data;
using Bank.DAL.Models.BaseModels;
using Bank.Services.AssemblyMarker;
using Hangfire;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;

namespace Bank
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 6;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 200L * 1024 * 1024;
            });
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 200L * 1024 * 1024;
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowFrontend", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            builder.Services.AddApplicationServices(builder.Configuration);
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("CanDownload", policy =>
                    policy.RequireAssertion(context =>
                        context.User.IsInRole("Admin") ||
                        context.User.IsInRole("SuperAdmin") ||
                        context.User.HasClaim("CanDownload", "true")));

            

                options.AddPolicy("CanUploadDataFiles", policy =>
                    policy.RequireAssertion(context =>
                        context.User.IsInRole("Admin") ||
                        context.User.IsInRole("SuperAdmin") ||
                        context.User.HasClaim("CanUploadDataFiles", "true")));
                options.AddPolicy("CanShare", policy =>
                    policy.RequireAssertion(context =>
                        context.User.IsInRole("Admin") ||
                        context.User.IsInRole("SuperAdmin") ||
                        context.User.HasClaim("CanShare", "true")));
                options.AddPolicy("CanUploadCheckFile", policy =>
                    policy.RequireAssertion(context =>
                        context.User.IsInRole("Admin") ||
                        context.User.IsInRole("SuperAdmin") ||
                        context.User.HasClaim("CanUploadCheckFile", "true")));
            });
            builder.Services.AddMemoryCache();
            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                await IdentitySeeder.SeedRolesAsync(scope.ServiceProvider);
                await IdentitySeeder.AddAdminAsync(scope.ServiceProvider);
            }

            app.UseMiddleware<ErrorHandlingMiddleWare>();

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Base API v1");
            });

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCors("AllowFrontend");

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = new[] { new HangfireAuthorizationFilter() }
            });
            app.MapHub<LiveCheckHub>("/hubs/live-check");

            app.MapControllers();

            app.Run();
        }
    }
}
//using Bank.API.Filters;
//using Bank.API.MiddleWare;
//using Bank.API.Services;
//using Bank.DAL.Context;
//using Bank.DAL.Data;
//using Bank.DAL.Models.BaseModels;
//using Bank.Repo.Implementations;
//using Bank.Services.AssemblyMarker;
//using Bank.Services.Interfaces;
//using Hangfire;
//using Microsoft.AspNetCore.Http.Features;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.OpenApi;

//namespace Bank
//{
//    public class Program
//    {
//        public static async Task Main(string[] args)
//        {
//            var builder = WebApplication.CreateBuilder(args);
//            // في Program.cs
//            builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
//            {
//                options.Password.RequireDigit = false;
//                options.Password.RequireLowercase = false;
//                options.Password.RequireNonAlphanumeric = false;
//                options.Password.RequireUppercase = false;
//                options.Password.RequiredLength = 6;
//            })
//            .AddEntityFrameworkStores<AppDbContext>()
//            .AddDefaultTokenProviders();
//            builder.Services.AddCors(options =>
//            {
//                options.AddPolicy("AllowFrontend", policy =>
//                {
//                    policy.AllowAnyOrigin()
//              .AllowAnyHeader()
//              .AllowAnyMethod();
//                    //policy
//                    //    .WithOrigins(
//                    //        "http://localhost:3000",
//                    //        "http://localhost:5173"
//                    //    )
//                    //    .AllowAnyHeader()
//                    //    .AllowAnyMethod();
//                });
//            });



//            builder.Services.AddApplicationServices(builder.Configuration);




//            var app = builder.Build();
//            using (var scope = app.Services.CreateScope())
//            {
//                await IdentitySeeder.SeedRolesAsync(scope.ServiceProvider);
//                await IdentitySeeder.AddAdminAsync(scope.ServiceProvider);
//            }

//            //if (app.Environment.IsDevelopment())
//            //{
//            app.UseSwagger();
//                app.UseSwaggerUI(c =>
//                {
//                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Base API v1");
//                });
//           // }

//            app.UseHttpsRedirection();
//            app.UseMiddleware<ErrorHandlingMiddleWare>();
//            app.UseStaticFiles();
//            app.UseCors("AllowFrontend");

//            app.UseAuthentication();
//            app.UseAuthorization();


//            app.MapControllers();

//            app.Run();
//        }
//    }
//}

