using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;
using NetTopologySuite.Geometries;

namespace Bank.DAL.Models.SystemModels;

public class LiveCheckLog : BaseEntityLong
{
    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;
    public CheckType CheckType { get; set; } = CheckType.Live;

    public string? District { get; set; }
    public string? Street { get; set; }

    // SQL Server spatial point (Lat, Long)
    public Point? Location { get; set; }

    public bool IsMatched { get; set; }
    public long? MatchedCheckPlateId { get; set; }
    public virtual CheckPlate? MatchedCheckPlate { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}