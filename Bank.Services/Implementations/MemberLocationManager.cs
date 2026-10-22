using System.Collections.Concurrent;
using Bank.Services.Interfaces;

namespace Bank.Services.Implementations;

// ⭐ Singleton — لازم يتسجل AddSingleton. سيرفر واحد فبنخزّن في الذاكرة بس.
public class MemberLocationManager : IMemberLocationManager
{
    private readonly ConcurrentDictionary<string, Guid> _connections = new();
    private readonly ConcurrentDictionary<Guid, PendingRequest> _requests = new();
    private readonly Dictionary<Guid, DateTime> _lastRequestByAdmin = new();
    private readonly object _cooldownLock = new();

    private sealed class PendingRequest
    {
        // لكل مستهدف TaskCompletionSource بيتكمّل لما يرد (بمكانه أو بـ null = غير متاح).
        public Dictionary<Guid, TaskCompletionSource<(double Latitude, double Longitude)?>> Replies { get; }

        public PendingRequest(IEnumerable<Guid> targetUserIds)
        {
            Replies = targetUserIds.Distinct().ToDictionary(
                id => id,
                _ => new TaskCompletionSource<(double Latitude, double Longitude)?>(
                    TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    public void AddConnection(string connectionId, Guid userId) => _connections[connectionId] = userId;

    public void RemoveConnection(string connectionId) => _connections.TryRemove(connectionId, out _);

    public List<string> GetConnectionIds(Guid userId) =>
        _connections.Where(c => c.Value == userId).Select(c => c.Key).ToList();

    public bool TryStartCooldown(Guid adminUserId, TimeSpan cooldown, out TimeSpan retryAfter)
    {
        lock (_cooldownLock)
        {
            var now = DateTime.UtcNow;
            if (_lastRequestByAdmin.TryGetValue(adminUserId, out var last) && now - last < cooldown)
            {
                retryAfter = cooldown - (now - last);
                return false;
            }

            _lastRequestByAdmin[adminUserId] = now;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public Guid CreateRequest(IEnumerable<Guid> targetUserIds)
    {
        var requestId = Guid.NewGuid();
        _requests[requestId] = new PendingRequest(targetUserIds);
        return requestId;
    }

    public bool TryReportLocation(Guid requestId, Guid userId, double latitude, double longitude) =>
        Complete(requestId, userId, (latitude, longitude));

    public bool TryReportUnavailable(Guid requestId, Guid userId) =>
        Complete(requestId, userId, null);

    // الرد بيتقبل بس لو الطلب موجود والمستخدم ده مستهدف فيه فعلًا — فمحدش
    // يقدر يحقن مكان لحد تاني ولا يرد على طلب مش بتاعه.
    private bool Complete(Guid requestId, Guid userId, (double Latitude, double Longitude)? value) =>
        _requests.TryGetValue(requestId, out var request)
        && request.Replies.TryGetValue(userId, out var tcs)
        && tcs.TrySetResult(value);

    public async Task<Dictionary<Guid, (double Latitude, double Longitude)?>> WaitForRepliesAsync(
        Guid requestId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(requestId, out var request))
        {
            return new Dictionary<Guid, (double Latitude, double Longitude)?>();
        }

        var allReplied = Task.WhenAll(request.Replies.Values.Select(r => r.Task));

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, delayCts.Token);

        if (await Task.WhenAny(allReplied, delay) == allReplied)
        {
            delayCts.Cancel();
        }

        // اللي ردّ بيرجع بمكانه، واللي لسه ما ردّش بيرجع null (غير متاح).
        return request.Replies.ToDictionary(
            r => r.Key,
            r => r.Value.Task.IsCompletedSuccessfully ? r.Value.Task.Result : null);
    }

    public void RemoveRequest(Guid requestId) => _requests.TryRemove(requestId, out _);
}
