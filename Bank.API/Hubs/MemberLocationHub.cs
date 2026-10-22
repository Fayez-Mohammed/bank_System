using System.Security.Claims;
using Bank.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bank.API.Hubs;

/// <summary>
/// Hub منفصل تمامًا عن LiveCheckHub. تطبيق العضو بيتصل بيه وهو شغال وبيستنى طلب
/// "LocationRequested"، ولما يوصله بيقرا الـ GPS مرة واحدة ويرد (مفيش تتبع مستمر).
/// </summary>
[Authorize]
public class MemberLocationHub : Hub
{
    private readonly IMemberLocationManager _manager;

    public MemberLocationHub(IMemberLocationManager manager)
    {
        _manager = manager;
    }

    public override async Task OnConnectedAsync()
    {
        _manager.AddConnection(Context.ConnectionId, GetUserId());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _manager.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // رد العضو بمكانه الحالي على طلب معيّن.
    public Task ReportLocation(Guid requestId, double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude) ||
            latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return Task.CompletedTask;
        }

        _manager.TryReportLocation(requestId, GetUserId(), latitude, longitude);
        return Task.CompletedTask;
    }

    // العضو مقدرش يجيب مكانه (الـ GPS مقفول أو الصلاحية مرفوضة) — يرد بدل ما الأدمن يفضل مستني.
    public Task ReportUnavailable(Guid requestId)
    {
        _manager.TryReportUnavailable(requestId, GetUserId());
        return Task.CompletedTask;
    }

    private Guid GetUserId() => Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
