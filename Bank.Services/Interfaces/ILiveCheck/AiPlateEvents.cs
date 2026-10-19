namespace Bank.Services.Interfaces.ILiveCheck;

// أحداث خام جاية من اتصال الـ AI (WebSocket) — نوع واحد لكل شكل رسالة. ده
// مختلف عن AiPlateDetectedEvent الموجود أصلًا واللي بيستخدمه ProcessDetectionAsync
// والتشييك اليدوي؛ ده بالذات بيُستخدم جوه WebSocketLiveCheckAiSession/LiveCheckHub بس،
// وبيتحوّل لـ AiPlateDetectedEvent قبل ما يوصل للـ Service في حالة الاكتشاف العادي.
public abstract record AiPlateEvent(int TimestampMs);

public sealed record PlateDetectedEvent(
    string PlateNumber,
    string? VehicleType,
    int TimestampMs) : AiPlateEvent(TimestampMs);

public sealed record PlateCorrectionEvent(
    string NewPlateNumber,
    string? VehicleType,
    int TimestampMs) : AiPlateEvent(TimestampMs);

public sealed record PlateReplacementEvent(
    string OldPlateNumber,
    string NewPlateNumber,
    string? VehicleType,
    int TimestampMs) : AiPlateEvent(TimestampMs);
