using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class RefreshTokenConfiguration : BaseEntityLongConfiguration<RefreshToken>
{
    public override void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        base.Configure(builder);

        builder.ToTable("refresh_tokens");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Token).HasColumnName("token").HasMaxLength(255).IsRequired();
        builder.Property(x => x.JwtId).HasColumnName("jwt_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsUsed).HasColumnName("is_used").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsRevoked).HasColumnName("is_revoked").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IsRevoked, x.ExpiresAt });

        builder.HasOne(x => x.User)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}