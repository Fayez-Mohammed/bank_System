using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Bank.DAL.Context;
using Bank.DAL.Models.BaseModels;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.AuthDTOs;
using Bank.Shared.DTOs.UserDTOs;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations
{
    public class AuthService : IAuthService
    {
        private const string SessionExpiredMessage = "الجلسة انتهت، سجّل دخول تاني.";
        private const string DeviceMismatchMessage = "الحساب ده مربوط بجهاز تاني. اطلب من الأدمن يفك ارتباط الجهاز.";

        private readonly IUserService _userService;
        private readonly IJWTService _jWTService;
        private readonly IConfiguration _conf;
        private readonly AppDbContext _db;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserService userService, IJWTService jWTService, IConfiguration conf,
            AppDbContext db, ILogger<AuthService> logger)
        {
            _userService = userService;
            _jWTService = jWTService;
            _conf = conf;
            _db = db;
            _logger = logger;
        }

        private bool DeviceBindingEnabled =>
            !bool.TryParse(_conf["Auth:DeviceBinding:Enabled"], out var enabled) || enabled;

        private int RefreshTokenDays =>
            int.TryParse(_conf["Auth:JWT:RefreshTokenDays"], out var days) && days > 0 ? days : 30;

        public async Task<ApiResponse<RegisterResponseDTO>> RegisterAsync(RegisterDTO registerDTO)
        {
            if (registerDTO is null)
                return ApiResponse<RegisterResponseDTO>.Fail("please fill the form");
            var existingUser = await _userService.GetByEmailAsync(registerDTO.Email);
            if (existingUser is not null)
                return ApiResponse<RegisterResponseDTO>.Fail("Email Already Exist");
            ApplicationUser user = new ApplicationUser
            {
                FullName = registerDTO.FullName,
                Email = registerDTO.Email,
                UserName = registerDTO.Email,
                Role = UserRole.Admin

            };
            var Appuser = await _userService.CreateUserAsync(user, registerDTO.Password);
            var res = new RegisterResponseDTO
            {
                UserId = Appuser.Id.ToString(),
                Email = Appuser.Email
            };
            return ApiResponse<RegisterResponseDTO>.SuccessResponse(res, "User registered successfully", 201);
        }
        public async Task<ApiResponse<LoginResponseDTO>> LoginAsync(LoginDTO loginDTO)
        {
            if (loginDTO is null)
                return ApiResponse<LoginResponseDTO>.Fail("please fill the form");

            var user = await _userService.GetByEmailAsync(loginDTO.Email);
            if (user is null)
                return ApiResponse<LoginResponseDTO>.Fail("Email or password is not correct");

            bool isCorrect = await _userService.CheckPasswordAsync(user, loginDTO.Password);
            if (!isCorrect)
                return ApiResponse<LoginResponseDTO>.Fail("Email or password is not correct");

            if (!user.IsActive)
                return ApiResponse<LoginResponseDTO>.Fail("الحساب موقوف. تواصل مع الأدمن.", null, 403);

            // الجهاز بيتتشيك بعد الباسورد عشان محدش من غير باسورد يعرف حاجة عن الربط
            var deviceError = await EnforceDeviceAsync(
                user, HashDeviceId(loginDTO.DeviceId), loginDTO.DeviceName, bindIfEmpty: true);
            if (deviceError is not null)
                return ApiResponse<LoginResponseDTO>.Fail(deviceError, null, 403);

            // جلسة واحدة للمستخدم: أي refresh tokens قديمة بتتلغي
            await _db.RefreshTokens
                .Where(t => t.UserId == user.Id && !t.IsRevoked && !t.IsUsed)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true));

            var result = await IssueTokensAsync(user);
            return ApiResponse<LoginResponseDTO>.SuccessResponse(result, "User logged in successfully", 200);
        }

        public async Task<ApiResponse<LoginResponseDTO>> RefreshAsync(
            RefreshTokenRequestDTO request, CancellationToken ct = default)
        {
            var tokenHash = Sha256Hex(request.RefreshToken.Trim());

            var stored = await _db.RefreshTokens
                .AsNoTracking()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == tokenHash, ct);

            if (stored is null || stored.IsUsed || stored.IsRevoked || stored.ExpiresAt <= DateTime.UtcNow)
                return ApiResponse<LoginResponseDTO>.Fail(SessionExpiredMessage, null, 401);

            var user = stored.User;
            if (!user.IsActive)
                return ApiResponse<LoginResponseDTO>.Fail("الحساب موقوف. تواصل مع الأدمن.", null, 403);

            var deviceError = await EnforceDeviceAsync(
                user, HashDeviceId(request.DeviceId), null, bindIfEmpty: false, ct);
            if (deviceError is not null)
                return ApiResponse<LoginResponseDTO>.Fail(deviceError, null, 403);

            // استهلاك ذري: لو طلبين جم بنفس الـ token في نفس اللحظة، واحد بس بينجح
            var consumed = await _db.RefreshTokens
                .Where(t => t.Id == stored.Id && !t.IsUsed && !t.IsRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsUsed, true), ct);
            if (consumed == 0)
                return ApiResponse<LoginResponseDTO>.Fail(SessionExpiredMessage, null, 401);

            var result = await IssueTokensAsync(user, ct);
            return ApiResponse<LoginResponseDTO>.SuccessResponse(result, "Token refreshed", 200);
        }

        public async Task<ApiResponse<bool>> LogoutAsync(LogoutRequestDTO request, CancellationToken ct = default)
        {
            var tokenHash = Sha256Hex(request.RefreshToken.Trim());

            await _db.RefreshTokens
                .Where(t => t.Token == tokenHash && !t.IsRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true), ct);

            return ApiResponse<bool>.SuccessResponse(true, "تم تسجيل الخروج.");
        }

        public async Task<ApiResponse<bool>> ResetUserDeviceAsync(
            Guid actorId, Guid targetUserId, CancellationToken ct = default)
        {
            var actorRole = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == actorId && u.IsActive)
                .Select(u => (UserRole?)u.Role)
                .FirstOrDefaultAsync(ct);
            if (actorRole is null)
                return ApiResponse<bool>.Fail("المستخدم غير موجود.", null, 404);

            var targets = _db.Users.Where(u => u.Id == targetUserId);

            if (actorRole == UserRole.Admin)
            {
                var myGroupId = await _db.Groups
                    .AsNoTracking()
                    .Where(g => g.AdminId == actorId && g.IsActive)
                    .Select(g => (Guid?)g.Id)
                    .FirstOrDefaultAsync(ct);
                if (myGroupId is null)
                    return ApiResponse<bool>.Fail("مفيش جروب مسؤول عنه.", null, 400);

                // الأدمن يفك أجهزة أعضاء جروبه (Role = User) بس
                targets = targets.Where(u => u.GroupId == myGroupId && u.Role == UserRole.User);
            }
            else if (actorRole != UserRole.SuperAdmin)
            {
                return ApiResponse<bool>.Fail("غير مسموح.", null, 403);
            }

            var affected = await targets.ExecuteUpdateAsync(s => s
                .SetProperty(u => u.BoundDeviceHash, (string?)null)
                .SetProperty(u => u.BoundDeviceName, (string?)null)
                .SetProperty(u => u.DeviceBoundAt, (DateTime?)null), ct);
            if (affected == 0)
                return ApiResponse<bool>.Fail("المستخدم غير موجود.", null, 404);

            await _db.RefreshTokens
                .Where(t => t.UserId == targetUserId && !t.IsRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true), ct);

            _logger.LogInformation("Device binding reset for user {TargetUserId} by {ActorId}", targetUserId, actorId);
            return ApiResponse<bool>.SuccessResponse(true, "تم فك ارتباط الجهاز. المستخدم هيربط جهازه الجديد عند أول تسجيل دخول.");
        }

        // ---------- helpers ----------

        private async Task<string?> EnforceDeviceAsync(
            ApplicationUser user, string deviceHash, string? deviceName, bool bindIfEmpty,
            CancellationToken ct = default)
        {
            if (!DeviceBindingEnabled) return null;

            var boundHash = user.BoundDeviceHash;

            if (boundHash is null)
            {
                if (!bindIfEmpty) return SessionExpiredMessage;

                var name = Truncate(deviceName, 150);
                var now = DateTime.UtcNow;

                // ربط ذري: لو جهازين سجلوا أول مرة في نفس اللحظة، واحد بس بيكسب
                var bound = await _db.Users
                    .Where(u => u.Id == user.Id && u.BoundDeviceHash == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.BoundDeviceHash, deviceHash)
                        .SetProperty(u => u.BoundDeviceName, name)
                        .SetProperty(u => u.DeviceBoundAt, now), ct);

                if (bound == 1)
                {
                    _logger.LogInformation("User {UserId} bound to device '{DeviceName}'", user.Id, name);
                    return null;
                }

                boundHash = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == user.Id)
                    .Select(u => u.BoundDeviceHash)
                    .FirstOrDefaultAsync(ct);
            }

            if (IsSameDevice(boundHash, deviceHash)) return null;

            _logger.LogWarning("Login/refresh rejected for user {UserId}: different device", user.Id);
            return DeviceMismatchMessage;
        }

        private async Task<LoginResponseDTO> IssueTokensAsync(ApplicationUser user, CancellationToken ct = default)
        {
            var accessToken = await _jWTService.GenerateJWTToken(user);
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

            var rawRefreshToken = NewRefreshTokenValue();
            var refreshExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays);

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = Sha256Hex(rawRefreshToken),   // بنخزّن الـ hash بس
                JwtId = jwt.Id,
                ExpiresAt = refreshExpiresAt
            });
            await _db.SaveChangesAsync(ct);

            return new LoginResponseDTO
            {
                AccessToken = accessToken,
                ExpireAt = jwt.ValidTo,
                RefreshToken = rawRefreshToken,
                RefreshTokenExpireAt = refreshExpiresAt,
                UserLogin = new UserLoginDTO
                {
                    Id = user.Id.ToString(),
                    FullName = user.FullName,
                    Role = user.Role.ToString()
                }
            };
        }

        private static string HashDeviceId(string deviceId) => Sha256Hex(deviceId.Trim());

        private static string Sha256Hex(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static string NewRefreshTokenValue() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static bool IsSameDevice(string? boundHash, string deviceHash) =>
            boundHash is not null &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(boundHash), Encoding.UTF8.GetBytes(deviceHash));

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }
  
public async Task<ApiResponse<MyGroupDTO>> GetMyGroupAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return ApiResponse<MyGroupDTO>.Fail("UserId is required");
            var response = await _userService.GetMyGroupAsync(userId);
            return response;

        }
    }
}

