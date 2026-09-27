using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

/// <summary>
/// التنفيذ الحقيقي بتاع الاتصال بموديل صاحبك — لما يخلص ويديك الـ URL/handshake
/// الحقيقي، الملف ده بس اللي هيتعدّل. شكل الرسالة الحالي مطابق لللي أكّده:
/// { "type": "plate_detected", "plate_number": "...", "vehicle_type": "...", "timestamp_ms": ... }
/// </summary>
public class WebSocketLiveCheckAiConnector : ILiveCheckAiConnector
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public WebSocketLiveCheckAiConnector(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    public async Task<ILiveCheckAiSession> StartSessionAsync(CancellationToken cancellationToken = default)
    {
        var url = _configuration["LiveCheckAi:WebSocketUrl"]
            ?? throw new InvalidOperationException("LiveCheckAi:WebSocketUrl مش متظبطة في appsettings.");

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), cancellationToken);

        return new WebSocketLiveCheckAiSession(socket, _loggerFactory.CreateLogger<WebSocketLiveCheckAiSession>());
    }
}

public class WebSocketLiveCheckAiSession : ILiveCheckAiSession
{
    private readonly ClientWebSocket _socket;
    private readonly ILogger<WebSocketLiveCheckAiSession> _logger;

    public WebSocketLiveCheckAiSession(ClientWebSocket socket, ILogger<WebSocketLiveCheckAiSession> logger)
    {
        _socket = socket;
        _logger = logger;
    }

    public async Task SendAudioChunkAsync(byte[] chunk, CancellationToken cancellationToken = default)
    {
        if (_socket.State != WebSocketState.Open) return;
        await _socket.SendAsync(chunk, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
    }

    public async IAsyncEnumerable<AiPlateDetectedEvent> ReadDetectionsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[8192];

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var messageStream = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                messageStream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(messageStream.ToArray());

            AiRawMessage? message = null;
            var parseFailed = false;
            try
            {
                message = JsonSerializer.Deserialize<AiRawMessage>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not parse AI message: {Json}", json);
                parseFailed = true;
            }

            if (parseFailed || message is null || message.Type != "plate_detected" || string.IsNullOrWhiteSpace(message.PlateNumber))
            {
                // ⭐ بنتجاهل أي رسالة مش "plate_detected" — لو صاحبك ضاف أنواع
                // تانية بعدين (session_ready, error...) الكود هنا مش هينكسر.
                continue;
            }

            yield return new AiPlateDetectedEvent(message.PlateNumber!, message.VehicleType, message.TimestampMs ?? 0);
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record AiRawMessage(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("plate_number")] string? PlateNumber,
        [property: JsonPropertyName("vehicle_type")] string? VehicleType,
        [property: JsonPropertyName("timestamp_ms")] int? TimestampMs);
}
