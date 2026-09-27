namespace Bank.Shared.Enums;

// ⭐ إعادة تعريف نظيفة — القيم القديمة (Live, LiveVoice, Manual7Boxes) كانت
// متضاربة عبر تكرارات سابقة. دلوقتي بسيطة وواضحة: تشييك لحظي بالصوت، أو يدوي.
public enum CheckType
{
    Live,
    Manual
}
