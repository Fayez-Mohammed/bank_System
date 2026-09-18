using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

public class CheckPlate : BaseEntityLong
{
    public Guid CheckFileId { get; set; }
    public CheckFile CheckFile { get; set; } = null!;

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;   // 3 letters + 4 digits (zero-padded)

    public string BankName { get; set; } = null!;

    public string? VehicleType { get; set; }
    public string? ChassisNumber { get; set; }
    public string? ClaimStatus { get; set; }             

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<RecordedPlate> MatchedRecordedPlates { get; set; } = new List<RecordedPlate>();
    public ICollection<LiveCheckLog> MatchedLiveCheckLogs { get; set; } = new List<LiveCheckLog>();
}
