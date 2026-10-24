public static class DateTimeExtensions
{
    private static readonly TimeZoneInfo SaudiTimeZone = GetSaudiTimeZone();

    private static TimeZoneInfo GetSaudiTimeZone()
    {
        try
        {
            // يتوافق مع سيرفرات Linux / Docker
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch
        {
            // يتوافق مع بيئة بيئة تطوير Windows
            return TimeZoneInfo.FindSystemTimeZoneById("Arabian Standard Time");
        }
    }

    /// <summary>
    /// تحويل أي وقت UTC إلى التوقيت المحلي للمملكة العربية السعودية
    /// </summary>
    public static DateTime ToSaudiLocalTime(this DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, SaudiTimeZone);
    }
}