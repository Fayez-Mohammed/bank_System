using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

// ⭐ جديد بالكامل — بتجمّع كل لوحات جلسة تشييك واحدة (زي RecordingSession
// بالظبط، بس من غير صوت متخزن — كله لحظي).
public class CheckSession : BaseEntity
{
    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    // Snapshot لملف التشيك النشط وقت بدء الجلسة — نفس فكرة RecordingSession.CheckFileId
    public Guid? CheckFileId { get; set; }
    public virtual CheckFile? CheckFile { get; set; }

    public string District { get; set; } = null!;
    public string Street { get; set; } = null!;
    public string ExcelSheetName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string RecorderName { get; set; } = null!;
    public int TotalPlates { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Recording;
    public string? ProcessingError { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    public virtual ICollection<LiveCheckLog> Logs { get; set; } = new List<LiveCheckLog>();
}
