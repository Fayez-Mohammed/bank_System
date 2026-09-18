using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

public class RefreshToken : BaseEntityLong
{
    public Guid UserId { get; set; } 
    public ApplicationUser User { get; set; } = null!;

    public string Token { get; set; } = null!;
    public string JwtId { get; set; } = null!;
    public bool IsUsed { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
