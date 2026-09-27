using Bank.Services.Interfaces;

namespace Bank.Services.Interfaces;

/// <summary>
/// SignalR بيعمل instance جديدة من الـ Hub كل استدعاء method، فمش نقدر نخزن
/// حالة (زي اتصال الـ AI المفتوح) جوه الـ Hub نفسه. الحل القياسي: singleton
/// بيمسك الحالة دي مربوطة بـ ConnectionId.
/// </summary>
public class LiveCheckConnectionState
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? CheckFileId { get; set; }
    public string District { get; set; } = null!;
    public string Street { get; set; } = null!;
    public ILiveCheckAiSession AiSession { get; set; } = null!;
    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public CancellationTokenSource ListenCts { get; set; } = new();
}

public interface ILiveCheckConnectionManager
{
    void Add(string connectionId, LiveCheckConnectionState state);
    LiveCheckConnectionState? Get(string connectionId);
    void Remove(string connectionId);
}
