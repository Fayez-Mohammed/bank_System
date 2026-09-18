using Bank.DAL.Models.BaseModels;
using NetTopologySuite.Geometries;

namespace Bank.DAL.Models.SystemModels;

public class ManualCheck : BaseEntityLong
{
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;

    public string? VehicleType { get; set; }

    public Guid? CheckFileId { get; set; }
    public CheckFile? CheckFile { get; set; }

    public bool IsMatched { get; set; }

    public long? MatchedCheckPlateId { get; set; }
    public CheckPlate? MatchedCheckPlate { get; set; }

    public Point? Location { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}