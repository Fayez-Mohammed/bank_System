using Bank.Shared.DTOs;
using Bank.Shared.DTOs.MemberLocationDTOs;

namespace Bank.Services.Interfaces;

public interface IMemberLocationService
{
    // أدمن الجروب بس: بيبعت طلب لحظي لأعضاء جروبه ويرجّع مكان كل واحد في اللحظة دي.
    Task<ApiResponse<List<MemberLocationDto>>> RequestGroupLocationsAsync(
        Guid adminUserId, CancellationToken cancellationToken = default);
}

// بيبعت الطلب فعليًا لجهاز العضو. متفصل كـ interface عشان الـ Hub نفسه
// موجود في Bank.API، والـ Service في Bank.Services مينفعش يشاور على API.
public interface IMemberLocationRequestSender
{
    Task SendLocationRequestAsync(string connectionId, Guid requestId, CancellationToken cancellationToken = default);
}

// Singleton في الذاكرة: مين متصل دلوقتي، والطلبات اللي لسه مستنية ردود.
// مفيش أي حاجة من دي بتتخزن في الداتابيز.
public interface IMemberLocationManager
{
    void AddConnection(string connectionId, Guid userId);
    void RemoveConnection(string connectionId);
    List<string> GetConnectionIds(Guid userId);

    // حد أدنى بين كل ضغطتين من نفس الأدمن (كل ضغطة بتشغّل GPS على موبايلات الأعضاء)
    bool TryStartCooldown(Guid adminUserId, TimeSpan cooldown, out TimeSpan retryAfter);

    Guid CreateRequest(IEnumerable<Guid> targetUserIds);
    bool TryReportLocation(Guid requestId, Guid userId, double latitude, double longitude);
    bool TryReportUnavailable(Guid requestId, Guid userId);

    // بينتظر ردود كل المستهدفين، أو لحد ما المهلة تخلص — اللي أسرع.
    Task<Dictionary<Guid, (double Latitude, double Longitude)?>> WaitForRepliesAsync(
        Guid requestId, TimeSpan timeout, CancellationToken cancellationToken = default);

    void RemoveRequest(Guid requestId);
}
