using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Services.Interfaces;
using Bank.Services.Interfaces.ILiveCheck;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

/// <summary>
/// التنفيذ الحقيقي بتاع الاتصال بموديل صاحبك. الاتصال بيستقبل ٣ أنواع رسايل
/// على نفس الـ WebSocket (plate_detected, plate_correction, plate_replacement)
/// ويحوّلهم لأحداث مختلفة (AiPlateEvent hierarchy) بدل ما يحاول يضغطهم كلهم
/// في شكل واحد.
///
/// سيرفر الـ AI (plate_model) لما المتكلم يقول «تعديل»، القاعدة: هل الـ Backend استلم اللوحة الغلط ولا لأ؟
/// - لسه ماستلمهاش (مثلًا «لوحة1 تعديل لوحة2» في نفس النَفَس): السيرفر بيشيل لوحة1 وبيبعت لوحة2 بس
///   كـ plate_detected عادي — هي لوحة جديدة بالنسبة للـ Backend.
/// - استلمها بالفعل (نَفَس قبل كده، أو اتبعتت بدري وهو لسه بيتكلم): plate_replacement (القديمة ← الجديدة).
/// السيرفر عمره ما بيبعت plate_correction: هي بتعدّل آخر لوحة في الجلسة، ودي مش دايمًا الغلط
/// (ممكن التعديل يكون للوحة من ٢-٣ لوحات فاتت) — يعني ممكن تمسح لوحة صح. الـ case بتاعها فاضل هنا للتوافق بس.
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

    public async Task SendStopAsync(CancellationToken cancellationToken = default)
    {
        if (_socket.State != WebSocketState.Open) return;
        var stop = Encoding.UTF8.GetBytes("{\"type\":\"stop\"}");
        await _socket.SendAsync(stop, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    public async IAsyncEnumerable<AiPlateEvent> ReadDetectionsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[8192];

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var messageStream = new MemoryStream();
            WebSocketReceiveResult result;
            var isNonTextMessage = false;

            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    // الرسايل النصية (JSON) بس هي اللي فيها أحداث — أي حاجة تانية
                    // (binary غلط جاي على نفس القناة مثلًا) بنتجاهلها بهدوء.
                    _logger.LogWarning("Ignoring unexpected non-text AI message.");
                    isNonTextMessage = true;
                    break;
                }

                messageStream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (isNonTextMessage) continue;

            var json = Encoding.UTF8.GetString(messageStream.ToArray());

            AiRawMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<AiRawMessage>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not parse AI message: {Json}", json);
                continue;
            }

            if (message?.Type is null) continue;

            var timestamp = message.TimestampMs ?? 0;

            switch (message.Type.ToLowerInvariant())
            {
                case "plate_detected":
                    if (!string.IsNullOrWhiteSpace(message.PlateNumber))
                    {
                        yield return new PlateDetectedEvent(message.PlateNumber, message.VehicleType, timestamp);
                    }
                    break;

                case "plate_correction":
                    if (!string.IsNullOrWhiteSpace(message.NewPlateNumber))
                    {
                        yield return new PlateCorrectionEvent(message.NewPlateNumber, message.VehicleType, timestamp);
                    }
                    break;

                case "plate_replacement":
                    if (!string.IsNullOrWhiteSpace(message.OldPlateNumber) && !string.IsNullOrWhiteSpace(message.NewPlateNumber))
                    {
                        yield return new PlateReplacementEvent(message.OldPlateNumber, message.NewPlateNumber, message.VehicleType, timestamp);
                    }
                    break;

                case "error":
                    _logger.LogWarning("AI reported an error: {Message}", message.Message);
                    break;

                default:
                    // ⭐ أي نوع تاني (session_ready، إلخ) بيتتجاهل بهدوء، مش error —
                    // لو صاحبك ضاف نوع جديد بعدين الكود هنا مش هينكسر.
                    _logger.LogDebug("Ignoring unsupported AI event type: {Type}", message.Type);
                    break;
            }
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        // CloseReceived = الـ AI قفل من ناحيته بعد الـ stop — بنكمّل الـ handshake بس.
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
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
        [property: JsonPropertyName("new_plate_number")] string? NewPlateNumber,
        [property: JsonPropertyName("old_plate_number")] string? OldPlateNumber,
        [property: JsonPropertyName("vehicle_type")] string? VehicleType,
        [property: JsonPropertyName("timestamp_ms")] int? TimestampMs,
        [property: JsonPropertyName("message")] string? Message);
}
