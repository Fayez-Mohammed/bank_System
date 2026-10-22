namespace Bank.Shared.DTOs.MemberLocationDTOs;

// مكان عضو واحد في اللحظة اللي الأدمن داس فيها.
// IsAvailable = false (والإحداثيات null) لو العضو مش متصل، أو الـ GPS عنده
// مش شغال، أو معرّدش في المهلة المحددة.
public record MemberLocationDto(
    string FullName,
    double? Latitude,
    double? Longitude,
    bool IsAvailable);
