using Bank.API.Filters;
using Bank.DAL.Context;
using Bank.Repo.Implementations;
using Bank.Repo.Interfaces;
using Bank.Services.AssemblyMarker;
using Bank.Services.HangFireJobs;
using Bank.Services.Implementations;
using Bank.Services.Implementations.CertificateServices;
using Bank.Services.Interfaces;
using Bank.Services.Interfaces.ICertificate;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
namespace Bank.API.Services
{
    public static class ServiceCollectionExtentions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services,IConfiguration configuration)
        {
            services.AddDatabase(configuration);
            services.AddInfrastructureServices(configuration);
            services.AddSwaggerWithJWT();
            services.AddAuthenticationWithJWT(configuration);
            services.AddAPIBehavior(configuration);
            return services;
           
        }
        public static IServiceCollection AddDatabase(this IServiceCollection services,IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            services
                 .AddDbContext<AppDbContext>(op => op
                 //.UseLazyLoadingProxies(true)
                 .UseNpgsql(connectionString,
                  npgsqlOptions => npgsqlOptions.UseNetTopologySuite()));

            services.AddHangfire(configuration => configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        InvisibilityTimeout = TimeSpan.FromMinutes(30),
                        QueuePollInterval = TimeSpan.FromSeconds(1),
                        PrepareSchemaIfNecessary = true
                    }));

            //services.AddHangfireServer(options =>
            //{
            //    options.Queues = new[] { "bulk-import", "default" };
            //    options.WorkerCount = 2; 
            //});
            //services.AddHangfireServer(options =>
            //{

            //    // options.Queues = new[] { "default", "bulk-import", "recording-processing" };
            //    options.Queues = new[] { "default", "bulk-import", "recording-processing", "certificates" };

            //    options.WorkerCount = 4;
            //});
            // التسجيلات: شغل بيستنى الـ AI/GPU.
            // الـ GPU مشترك مع الـ Live، فما تزوّدش العدد من غير ما فريق الـ AI يختبر.
            services.AddHangfireServer(options =>
            {
                options.ServerName = "recordings";
                options.Queues = new[] { "recording-processing", "default" };
                options.WorkerCount = 4;
            });

            // مزامنة الشهادات + الـ OCR (كل ساعة). 2 = Certificates:OcrEngineCount الافتراضي
            services.AddHangfireServer(options =>
            {
                options.ServerName = "certificates";
                options.Queues = new[] { "certificates" };
                options.WorkerCount = 2;
            });

            // الاستيراد والمطابقة (queue الـ bulk-import): بيشتغلوا واحد ورا التاني
            services.AddHangfireServer(options =>
            {
                options.ServerName = "imports";
                options.Queues = new[] { "bulk-import" };
                options.WorkerCount = 1;
            });
            services.AddScoped<DataFileImportJob>();
            return services;
        }
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,IConfiguration configuration)
        {
          
            services.AddFluentValidationAutoValidation();
            services.AddValidatorsFromAssemblyContaining<ApplicationAssemblyMarker>();
            services.AddAutoMapper(au => { },typeof(ApplicationAssemblyMarker).Assembly);
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IJWTService, JWTService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IFileStorageService, LocalFileStorageService>();
            services.AddScoped<IDataFileService, DataFileService>();
            services.AddScoped<IPermissionService, PermissionService>();
            services.AddScoped<IAdminService, AdminService>();
            services.AddScoped<ISuperAdminService, SuperAdminService>();
            services.AddScoped<IReferralJobService, ReferralJobService>();
            services.AddScoped<ICheckFileService, CheckFileService>();
            services.AddScoped<IPlateColumnDetector, PlateColumnDetector>();
            services.AddScoped<ICheckPlateCacheService, CheckPlateCacheService>();
            services.AddScoped<ILookupService, LookupService>();
            services.AddScoped<DataFileImportJob>();
            services.AddScoped<ReferralMatchingJob>();
            services.AddScoped<IRecordingArchiveService, RecordingArchiveService>();
            services.AddScoped<IRecordingSessionService, RecordingSessionService>();
            services.AddHttpClient<IAiTranscriptionService, AiTranscriptionService>();

            services.AddSignalR();

            services.AddSingleton<ILiveCheckConnectionManager,LiveCheckConnectionManager>();

            services.AddScoped<ILiveCheckAiConnector,WebSocketLiveCheckAiConnector>();

            services.AddScoped<ILiveCheckSessionService,LiveCheckSessionService>();

            services.AddSingleton<IGoogleDriveClient, GoogleDriveClient>();     // Singleton: بدون DB، حالة الربط في الذاكرة
            services.AddSingleton<ICertificateExtractor, CertificateExtractor>(); // Singleton: Pool لمحركات OCR
            services.AddScoped<ICertificateService, CertificateService>();
            services.AddScoped<CertificateSyncJob>();
            services.AddScoped<CertificateExtractionJob>();
            services.AddScoped<IMapService, MapService>();
            services.AddSingleton<IMemberLocationManager, MemberLocationManager>();
            services.AddScoped<IMemberLocationService, MemberLocationService>();
            services.AddSingleton<IMemberLocationRequestSender, SignalRMemberLocationRequestSender>();
            //notfications 
         


            // لو مش مسجّل بالفعل من الباتش اللي فات (استخراج الإحداثيات):
            services.AddHttpClient("MapsLinkResolver", client => client.Timeout = TimeSpan.FromSeconds(5))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            // وتأكد إن الـ queue "certificates" في options.Queues بتاع AddHangfireServer

            return services;
        }
        public static IServiceCollection AddAPIBehavior(this IServiceCollection services,IConfiguration configuration)
        {
            services.AddControllers(x =>
            {
                x.Filters.Add<ErrorFilter>();
                x.Filters.Add<ResponseWrapperFilter>();
            }).AddJsonOptions(op =>
            {
                op.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            services.AddOpenApi();
            services.AddEndpointsApiExplorer();
            return services;
        }
        public static IServiceCollection AddSwaggerWithJWT(this IServiceCollection services)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Bank API",
                    Version = "v1",
                    Description = "Bank API with JWT Authentication"
                });

                // Include XML Comments
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }

                // JWT Authentication Scheme
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter your JWT token"
                });

                // Security Requirement (OpenAPI v2 style)
                c.AddSecurityRequirement(document =>
                {
                    return new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
                    };
                });
            });

            return services;
        }

        public static IServiceCollection AddAuthenticationWithJWT(this IServiceCollection services, IConfiguration configuration)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuration["Auth:JWT:Key"]
                    ?? throw new InvalidOperationException("JWT Key is missing")));
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    JwtBearerDefaults.AuthenticationScheme;

                options.DefaultChallengeScheme =
                    JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,

                    ValidateIssuer = false,
                   // ValidIssuer = configuration["Auth:JWT:Issuer"],

                    ValidateAudience = false,
                   // ValidAudience = configuration["Auth:JWT:Audience"],

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30) 
                };
                options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        if (!Guid.TryParse(context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                        {
                            context.Fail("Invalid token.");
                            return;
                        }

                        var services = context.HttpContext.RequestServices;
                        var cache = services.GetRequiredService<IMemoryCache>();

                        // الـ cache بيوفّر query على كل request (60 ثانية)، والـ toggle بيمسحه فالإيقاف بيسري فورًا
                        var isActive = await cache.GetOrCreateAsync($"user-active:{userId}", async entry =>
                        {
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                            var db = services.GetRequiredService<AppDbContext>();
                            return await db.Users.AsNoTracking()
                                .Where(u => u.Id == userId)
                                .Select(u => u.IsActive)
                                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                        });

                        if (!isActive) context.Fail("Account disabled.");
                    }
                };
            });


            return services;
        }

    }
}

