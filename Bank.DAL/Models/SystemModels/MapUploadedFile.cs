using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

// ملف Excel مرفوع لزر "الملف المرفوع" — صف واحد بس لكل مساحة عمل (جروب أو
// يوزر مستقل)، بيتستبدل بالكامل (هو وصفوفه) عند رفع ملف جديد.
public class MapUploadedFile : BaseEntity
{
    public Guid? GroupId { get; set; }
    public Guid UploadedBy { get; set; }

    public string FileName { get; set; } = null!;
    public int RowCount { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MapUploadedPlateRow> Rows { get; set; } = new List<MapUploadedPlateRow>();
}

// نفس فكرة الأعمدة الثابتة لملف الداتا (HistoricalPlate) + أعمدة متغيرة في JSON.
// الإحداثيات بتتحل مرة واحدة وقت الرفع وتتخزن هنا — مش بتتحل تاني في كل عرض.
public class MapUploadedPlateRow : BaseEntityLong
{
    public Guid MapUploadedFileId { get; set; }
    public MapUploadedFile MapUploadedFile { get; set; } = null!;

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;
    public string? VehicleType { get; set; }
    public string? District { get; set; }
    public string? Street { get; set; }
    public string? Notes { get; set; }
    public string? RecorderName { get; set; }
    public string? RecordedAt { get; set; }

    public string? LocationLink { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // باقي أعمدة الملف المتغيرة (اسم العمود -> القيمة)، jsonb في Postgres
    public string? ExtraDataJson { get; set; }
    public string? DataFileName { get; set; }
}
