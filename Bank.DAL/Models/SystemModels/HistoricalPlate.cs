using Bank.DAL.Models.BaseModels;
using NetTopologySuite.Geometries;

namespace Bank.DAL.Models.SystemModels;

public class HistoricalPlate : BaseEntityLong
{
    public Guid DataFileId { get; set; } 
    public DataFile DataFile { get; set; } = null!;

    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;
    public string? VehicleType { get; set; }
    public string? District { get; set; }
    public string? Street { get; set; }

    // SQL Server spatial point (Lat, Long)
    public string? Location { get; set; }
    //public DateTime? RecordedAt { get; set; }
    public string? RecordedAt { get; set; }
    public ICollection<MatchResult> MatchResults { get; set; } = new List<MatchResult>();
}
