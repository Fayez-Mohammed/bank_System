using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

public class MatchResult : BaseEntityLong
{
    public Guid ReferralJobId { get; set; }
    public ReferralJob ReferralJob { get; set; } = null!;

    public long HistoricalPlateId { get; set; }
    public HistoricalPlate HistoricalPlate { get; set; } = null!;

    public string NormalizedPlate { get; set; } = null!;
    public string? FoundVehicleType { get; set; }
    public string? FoundDistrict { get; set; }
    public string? FoundStreet { get; set; }


    public string? Location { get; set; }

    public string? FoundRecordedAt { get; set; }

    public Guid SourceDataFileId { get; set; }
    public DataFile SourceDataFile { get; set; } = null!;

    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;
}
