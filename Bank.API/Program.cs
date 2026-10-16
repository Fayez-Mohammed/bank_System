using Bank.API.Filters;
using Bank.API.Hubs;
using Bank.API.MiddleWare;
using Bank.API.Services;
using Bank.DAL.Context;
using Bank.DAL.Data;
using Bank.DAL.Models.BaseModels;
using Bank.Services.AssemblyMarker;
using Bank.Services.HangFireJobs;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
//using Serilog;
//using Serilog.Events;
namespace Bank
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

        //    Log.Logger = new LoggerConfiguration()
        //.ReadFrom.Configuration(builder.Configuration)
        //.Enrich.FromLogContext()

        //.MinimumLevel.Information()
        //.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        //.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        //.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Error)
        //.MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
        //.WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)

        //.WriteTo.File(
        //    path: Path.Combine(builder.Environment.ContentRootPath, "Logs", "app-log-.txt"),
        //    rollingInterval: RollingInterval.Day,
        //    retainedFileCountLimit: 1,
        //    fileSizeLimitBytes: 50 * 1024 * 1024,
        //    rollOnFileSizeLimit: true,
        //    shared: true,
        //    restrictedToMinimumLevel: LogEventLevel.Information // أو اجعله LogEventLevel.Warning لو أردت فقط تحذيرات وأخطاء
        //)
        //.CreateLogger();

        //    builder.Host.UseSerilog();


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
                options.AddPolicy("CanSort", policy =>
                    policy.RequireAssertion(context =>
                        context.User.IsInRole("Admin") ||
                        context.User.IsInRole("SuperAdmin") ||
                        context.User.HasClaim("CanSort", "true")));
            });
            builder.Services.AddMemoryCache();
            var dpKeysPath = builder.Configuration["DataProtection:KeysPath"];
            
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
            RecurringJob.AddOrUpdate<CertificateSyncJob>(
                "certificate-sync",
                job => job.SyncAsync(CancellationToken.None),
                app.Configuration["Certificates:SyncCron"] ?? "*/10 * * * *");
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

