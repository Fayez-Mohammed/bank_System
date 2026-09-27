using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;


public class ReferralPlate : BaseEntityLong
{
    public Guid ReferralJobId { get; set; }
    public ReferralJob ReferralJob { get; set; } = null!;

    public string RawPlate { get; set; } = null!;
    public string NormalizedPlate { get; set; } = null!;

    public ReferralPlateStatus Status { get; set; }
    public string? ExtraDataJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
