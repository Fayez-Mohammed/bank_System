using System.Collections.Concurrent;
using Bank.Services.Interfaces;

namespace Bank.Services.Implementations;

// ⭐ Singleton — لازم يتسجل AddSingleton مش Scoped، عشان يفضل نفس الـ
// dictionary طول عمر التطبيق مهما كان عدد الـ Hub instances اللي SignalR بيعملها.
public class LiveCheckConnectionManager : ILiveCheckConnectionManager
{
    private readonly ConcurrentDictionary<string, LiveCheckConnectionState> _connections = new();

    public void Add(string connectionId, LiveCheckConnectionState state) => _connections[connectionId] = state;

    public LiveCheckConnectionState? Get(string connectionId) =>
        _connections.TryGetValue(connectionId, out var state) ? state : null;

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);
}
