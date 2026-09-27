namespace Bank.Services.Interfaces;

/// <summary>
/// الجزء الوحيد اللي هيتغيّر لما موديل صاحبك يخلص فعليًا — الـ Hub وباقي
/// الكود بيتعاملوا مع الـ abstraction ده بس، مش مع تفاصيل WebSocket مباشرة.
/// </summary>
public record AiPlateDetectedEvent(string PlateNumber, string? VehicleType, int TimestampMs);

public interface ILiveCheckAiSession : IAsyncDisposable
{
    Task SendAudioChunkAsync(byte[] chunk, CancellationToken cancellationToken = default);

    /// <summary>بيستريم النتايج أول ما توصل — استخدمها بـ await foreach.</summary>
    IAsyncEnumerable<AiPlateDetectedEvent> ReadDetectionsAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

public interface ILiveCheckAiConnector
{
    Task<ILiveCheckAiSession> StartSessionAsync(CancellationToken cancellationToken = default);
}
