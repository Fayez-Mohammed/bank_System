using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

// نسخة دائمة من نتيجة كل تفريغ (تسجيل صوتي أو تشييك يدوي) بتاعة اليوزر، عبر الوقت كله.
// عمدًا من غير علاقة/FK حقيقية بـ RecordingSession أو RecordedPlate — حذف أو تفريغ
// السيشنز ميأثرش على الجدول ده أبدًا. SourceSessionId مرجعي بس، بيتستخدم وقت
// "التفريغ تاني" عشان نشيل نسخة الجلسة القديمة من الأرشيف ونكتب الجديدة بدالها.
public class RecordingArchivePlate : BaseEntityLong
{
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;
    public string? VehicleType { get; set; }
    public string? District { get; set; }
    public string? Street { get; set; }
    public string? Notes { get; set; }
    public string? Location { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? RecorderName { get; set; }

    public Guid? SourceSessionId { get; set; }
    public bool IsMatched { get; set; } = false;

    // لو الصف ده اتحوّل لملف داتا قبل كده، عشان المرة الجاية نلحق الجديد بس
    // (التحويل تراكمي ومش بيفضّي الأرشيف).
    public Guid? AppendedToDataFileId { get; set; }
    public DateTime? AppendedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
