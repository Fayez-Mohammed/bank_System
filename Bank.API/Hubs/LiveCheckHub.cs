using System.Security.Claims;
using Bank.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Bank.API.Hubs;

/// <summary>
/// كل اتصال Flutter بيعمله هنا بيمر بالمراحل: StartSession → SendAudioChunk*
/// (وUpdateLocation* بالتوازي) → StopSession (أو انقطاع مفاجئ بيتعامل معاه OnDisconnectedAsync).
///
/// ملحوظة أداء/تصميم مهمة: SignalR بيعمل instance جديدة من الكلاس ده لكل
/// استدعاء method، فمينفعش نخزّن حالة (زي اتصال الـ AI المفتوح) كـ instance
/// field عادي — عشان كده استخدمنا ILiveCheckConnectionManager (singleton)
/// بيمسك الحالة دي مربوطة بـ Context.ConnectionId.
/// </summary>
[Authorize]
public class LiveCheckHub : Hub
{
    private readonly ILiveCheckSessionService _sessionService;
    private readonly ILiveCheckAiConnector _aiConnector;
    private readonly ILiveCheckConnectionManager _connections;
    private readonly IHubContext<LiveCheckHub> _hubContext;
    private readonly ILogger<LiveCheckHub> _logger;

    public LiveCheckHub(
        ILiveCheckSessionService sessionService, ILiveCheckAiConnector aiConnector,
        ILiveCheckConnectionManager connections, IHubContext<LiveCheckHub> hubContext, ILogger<LiveCheckHub> logger)
    {
        _sessionService = sessionService;
        _aiConnector = aiConnector;
        _connections = connections;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartSession(string district, string street, string? excelSheetName)
    {
        var userId = GetUserId();

        try
        {
            var (session, checkFileId) = await _sessionService.CreateSessionAsync(userId, district, street, excelSheetName);
            var aiSession = await _aiConnector.StartSessionAsync(Context.ConnectionAborted);

            var state = new LiveCheckConnectionState
            {
                SessionId = session.Id,
                UserId = userId,
                GroupId = session.GroupId,
                CheckFileId = checkFileId,
                District = district,
                Street = street,
                AiSession = aiSession
            };

            _connections.Add(Context.ConnectionId, state);

            // مش بننتظر الـ Task ده هنا — الـ Hub method لازم يرجع بسرعة، والاستماع
            // للنتايج شغال في الخلفية طول عمر الجلسة.
            _ = ListenForDetectionsAsync(Context.ConnectionId, state);

            await Clients.Caller.SendAsync("SessionStarted", new { sessionId = session.Id, displayName = session.DisplayName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start live check session for user {UserId}", userId);
            await Clients.Caller.SendAsync("Error", new { message = "تعذّر بدء جلسة التشييك." });
        }
    }

    public async Task SendAudioChunk(byte[] chunk)
    {
        var state = _connections.Get(Context.ConnectionId);
        if (state is null) return;

        try
        {
            await state.AiSession.SendAudioChunkAsync(chunk, Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forward audio chunk for connection {ConnectionId}", Context.ConnectionId);
        }
    }

    public Task UpdateLocation(double latitude, double longitude)
    {
        var state = _connections.Get(Context.ConnectionId);
        if (state is not null)
        {
            state.LastLatitude = latitude;
            state.LastLongitude = longitude;
        }
        return Task.CompletedTask;
    }

    public async Task StopSession()
    {
        var state = _connections.Get(Context.ConnectionId);
        if (state is null) return;

        await FinalizeSessionAsync(Context.ConnectionId, state);
        await Clients.Caller.SendAsync("SessionStopped", new { sessionId = state.SessionId });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var state = _connections.Get(Context.ConnectionId);
        if (state is not null)
        {
            _logger.LogInformation("Connection {ConnectionId} dropped unexpectedly — finalizing CheckSession {SessionId}", Context.ConnectionId, state.SessionId);
            await FinalizeSessionAsync(Context.ConnectionId, state);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task ListenForDetectionsAsync(string connectionId, LiveCheckConnectionState state)
    {
        try
        {
            await foreach (var detection in state.AiSession.ReadDetectionsAsync(state.ListenCts.Token))
            {
                var result = await _sessionService.ProcessDetectionAsync(
                    state.SessionId, state.UserId, state.GroupId, state.CheckFileId, state.District, state.Street,
                    state.LastLatitude, state.LastLongitude, detection, Bank.Shared.Enums.CheckType.Live, CancellationToken.None);

                await _hubContext.Clients.Client(connectionId).SendAsync("PlateChecked", result);
            }
        }
        catch (OperationCanceledException)
        {
            // متوقع لما StopSession/Disconnect يلغي الـ CancellationToken — مش error حقيقي
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI listen loop failed for connection {ConnectionId}", connectionId);
            await _hubContext.Clients.Client(connectionId).SendAsync("AiDisconnected", new { message = ex.Message });
            await _sessionService.MarkSessionFailedAsync(state.SessionId, ex.Message, CancellationToken.None);
            _connections.Remove(connectionId);
        }
    }

    private async Task FinalizeSessionAsync(string connectionId, LiveCheckConnectionState state)
    {
        state.ListenCts.Cancel();

        try
        {
            await state.AiSession.CloseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing AI session for connection {ConnectionId}", connectionId);
        }

        await _sessionService.CompleteSessionAsync(state.SessionId, CancellationToken.None);
        _connections.Remove(connectionId);
    }

    private Guid GetUserId() => Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
