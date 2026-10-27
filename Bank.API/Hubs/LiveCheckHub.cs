using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Services.Interfaces.ILiveCheck;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly TimeSpan AiStopTimeout = TimeSpan.FromSeconds(10);

    public LiveCheckHub(
        ILiveCheckSessionService sessionService, ILiveCheckAiConnector aiConnector,
        ILiveCheckConnectionManager connections, IHubContext<LiveCheckHub> hubContext, ILogger<LiveCheckHub> logger,
        IServiceScopeFactory scopeFactory)
    {
        _sessionService = sessionService;
        _aiConnector = aiConnector;
        _connections = connections;
        _hubContext = hubContext;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // اسم المسجل والحي والشارع اختياريين — الموبايل يقدر يبعت null أو "".
    public async Task StartSession(string? recorderName, string? district, string? street, string? excelSheetName)
    {
        var userId = GetUserId();
        Guid? createdSessionId = null;
        try
        {

            var (session, checkFileId) = await _sessionService.CreateSessionAsync(userId, recorderName, district, street, excelSheetName);
            var aiSession = await _aiConnector.StartSessionAsync(Context.ConnectionAborted);
            createdSessionId = session.Id;
            var state = new LiveCheckConnectionState
            {
                SessionId = session.Id,
                UserId = userId,
                GroupId = session.GroupId,
                CheckFileId = checkFileId,
                District = session.District,
                Street = session.Street,
                AiSession = aiSession
            };

            _connections.Add(Context.ConnectionId, state);

            
            state.ListenTask = ListenForDetectionsAsync(Context.ConnectionId, state);

            await Clients.Caller.SendAsync("SessionStarted", new { sessionId = session.Id, displayName = session.DisplayName, recorderName = session.RecorderName ,District = session.District,
                Street = session.Street
            });
        }
        catch (Exception ex)
        {
         
            _logger.LogError(ex, "Failed to start live check session for user {UserId}", userId);
            if (createdSessionId.HasValue)
            {
                try
                {
                    await _sessionService.MarkSessionFailedAsync(
                        createdSessionId.Value,
                        "تعذّر الاتصال بخدمة الـ AI أثناء بدء الجلسة.",
                        CancellationToken.None);

                    // await _sessionService.DeleteSessionAsync(createdSessionId.Value, CancellationToken.None);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback/clean up CheckSession {SessionId}", createdSessionId.Value);
                }
            }
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
            await foreach (var plateEvent in state.AiSession.ReadDetectionsAsync(state.ListenCts.Token))
            {
                // الـ loop ده عايش طول الجلسة، بعد ما scope الـ StartSession (والـ DbContext بتاعه)
                // اتقفل — فكل حدث بياخد scope جديد بدل _sessionService بتاع الـ Hub.
                using var scope = _scopeFactory.CreateScope();
                var sessionService = scope.ServiceProvider.GetRequiredService<ILiveCheckSessionService>();

                // الـ ٣ أنواع بترجع نفس شكل الـ DTO (LiveCheckPlateResultDto) وبتتبعت
                // كلهم تحت نفس إيفنت "PlateChecked" — الفلاتر بيعمل upsert بالـ Id
                // اللي جوّه النتيجة: Id جديد = صف جديد، Id موجود قبل كده = تحديث لصف
                // ظاهر بالفعل (ده حال الـ correction والـ replacement).
                var result = plateEvent switch
                {
                    PlateDetectedEvent detected => await sessionService.ProcessDetectionAsync(
                        state.SessionId, state.UserId, state.GroupId, state.CheckFileId, state.District, state.Street,
                        state.LastLatitude, state.LastLongitude,
                        new AiPlateDetectedEvent(detected.PlateNumber, detected.VehicleType, detected.TimestampMs),
                        Bank.Shared.Enums.CheckType.Live, state.ListenCts.Token),

                    PlateCorrectionEvent correction => await sessionService.ProcessCorrectionAsync(
                        state.SessionId, state.CheckFileId, correction.NewPlateNumber, correction.VehicleType, state.ListenCts.Token),

                    PlateReplacementEvent replacement => await sessionService.ProcessReplacementAsync(
                        state.SessionId, state.CheckFileId, replacement.OldPlateNumber, replacement.NewPlateNumber,
                        replacement.VehicleType, state.ListenCts.Token),

                    _ => null
                };

                if (result is null)
                {
                    // الـ Service بيسجّل اللوج بنفسه لو رفض الحدث (مثلًا تصحيح وصل
                    // من غير اكتشاف سابق، أو replacement للوحة مش موجودة في الجلسة).
                    continue;
                }

                await _hubContext.Clients.Client(connectionId).SendAsync("PlateChecked", result);
            }
        }
        catch (OperationCanceledException)
        {
            // متوقع لما StopSession/Disconnect يلغي الـ CancellationToken — مش error حقيقي
        }
        catch (Exception ex) when (state.ListenCts.IsCancellationRequested)
        {
            // الإلغاء ممكن يوصل من الـ DB driver كـ exception تاني غير OperationCanceledException
            _logger.LogWarning(ex, "AI listen loop interrupted by stop for connection {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI listen loop failed for connection {ConnectionId}", connectionId);
            state.Failed = true;
            await _hubContext.Clients.Client(connectionId).SendAsync("AiDisconnected", new { message = ex.Message });
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ILiveCheckSessionService>()
                .MarkSessionFailedAsync(state.SessionId, ex.Message, CancellationToken.None);
            _connections.Remove(connectionId);
        }
    }

    private async Task FinalizeSessionAsync(string connectionId, LiveCheckConnectionState state)
    {
        // الـ AI بيمسك آخر كلام لحد ما يتأكد إنه خلص، فلو قفلنا على طول اللوحة اللي اتقالت
        // قبل الإيقاف مباشرة بتضيع. بنبعت stop ونستنى السيرفر يبعت الباقي ويقفل (بحد أقصى).
        try
        {
            using var stopCts = new CancellationTokenSource(AiStopTimeout);
            await state.AiSession.SendStopAsync(stopCts.Token);
            await Task.WhenAny(state.ListenTask, Task.Delay(AiStopTimeout));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending stop to AI for connection {ConnectionId}", connectionId);
        }

        state.ListenCts.Cancel();

        // WhenAny مش بيوقف الـ listener — لازم نستناه يخلص فعلًا عشان مفيش PlateChecked
        // أو كتابة في الجلسة توصل بعد ما نقفلها.
        try
        {
            await state.ListenTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI listen loop ended with error for connection {ConnectionId}", connectionId);
        }

        try
        {
            await state.AiSession.CloseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing AI session for connection {ConnectionId}", connectionId);
        }

        // لو الـ listener فشل (حتى أثناء الـ stop flush) الجلسة اتعلّمت Failed — منكتبش عليها Completed.
        if (!state.Failed)
            await _sessionService.CompleteSessionAsync(state.SessionId, CancellationToken.None);
        _connections.Remove(connectionId);
    }

    private Guid GetUserId() => Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
