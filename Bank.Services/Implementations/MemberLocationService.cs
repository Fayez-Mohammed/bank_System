using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.MemberLocationDTOs;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

public class MemberLocationService : IMemberLocationService
{
    // أقصى وقت ننتظره عضو متصل بس مردّش (GPS بطيء أو مقفول). لو كلهم ردوا قبلها بنرجّع فورًا.
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _dbContext;
    private readonly IMemberLocationManager _manager;
    private readonly IMemberLocationRequestSender _sender;
    private readonly ILogger<MemberLocationService> _logger;

    public MemberLocationService(
        AppDbContext dbContext, IMemberLocationManager manager,
        IMemberLocationRequestSender sender, ILogger<MemberLocationService> logger)
    {
        _dbContext = dbContext;
        _manager = manager;
        _sender = sender;
        _logger = logger;
    }

    public async Task<ApiResponse<List<MemberLocationDto>>> RequestGroupLocationsAsync(
        Guid adminUserId, CancellationToken cancellationToken = default)
    {
        // الصلاحية بتتفحص من الداتابيز وقت الطلب (مش من التوكن).
        var admin = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == adminUserId)
            .Select(u => new {  u.Role })
            .FirstOrDefaultAsync(cancellationToken);
        var GroupId= await _dbContext.Groups.Where(g=>g.AdminId == adminUserId).Select(g => g.Id).FirstOrDefaultAsync(cancellationToken);
        if (admin is null) return ApiResponse<List<MemberLocationDto>>.Fail("المستخدم غير موجود.");

        if ( admin.Role != UserRole.Admin)
        {
            return ApiResponse<List<MemberLocationDto>>.Fail("الميزة دي متاحة لأدمن الجروب بس.", null, 403);
        }

        if (!_manager.TryStartCooldown(adminUserId, Cooldown, out var retryAfter))
        {
            return ApiResponse<List<MemberLocationDto>>.Fail(
                $"استنى {Math.Ceiling(retryAfter.TotalSeconds)} ثواني قبل ما تطلب تاني.", null, 429);
        }

        var members = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.GroupId == GroupId && u.Id != adminUserId && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(cancellationToken);

        if (members.Count == 0)
        {
            return ApiResponse<List<MemberLocationDto>>.SuccessResponse(new List<MemberLocationDto>());
        }

        // اللي مفيش اتصال نشط عنده بيبقى "غير متاح" فورًا — مش بننتظره.
        var connectedIds = members
            .Where(m => _manager.GetConnectionIds(m.Id).Count > 0)
            .Select(m => m.Id)
            .ToList();

        var requestId = _manager.CreateRequest(connectedIds);
        Dictionary<Guid, (double Latitude, double Longitude)?> replies;

        try
        {
            var sends = connectedIds
                .SelectMany(userId => _manager.GetConnectionIds(userId))
                .Select(connectionId => _sender.SendLocationRequestAsync(connectionId, requestId, cancellationToken));

            await Task.WhenAll(sends);

            replies = await _manager.WaitForRepliesAsync(requestId, ReplyTimeout, cancellationToken);
        }
        finally
        {
            _manager.RemoveRequest(requestId);
        }

        var result = members.Select(m =>
            replies.TryGetValue(m.Id, out var location) && location.HasValue
                ? new MemberLocationDto(m.FullName, location.Value.Latitude, location.Value.Longitude, true)
                : new MemberLocationDto(m.FullName, null, null, false))
            .ToList();

        // بنسجّل الأعداد بس — المواقع نفسها مش بتتسجل ولا بتتخزن في أي مكان.
        _logger.LogInformation(
            "Group {GroupId}: admin {AdminId} requested member locations — {Total} members, {Connected} connected, {Replied} replied",
            GroupId, adminUserId, members.Count, connectedIds.Count, result.Count(r => r.IsAvailable));

        return ApiResponse<List<MemberLocationDto>>.SuccessResponse(result);
    }
}
