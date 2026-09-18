namespace Bank.Shared.Enums;

public enum SessionStatus
{
    Pending,     // اتعمل، لسه التسجيل الصوتي ما بدأش
    Recording,   // التسجيل شغال دلوقتي
    Uploading,   // الصوت بيترفع
    Processing,  // في انتظار/تنفيذ الـ AI
    Completed,
    Failed,
    Cancelled
}
