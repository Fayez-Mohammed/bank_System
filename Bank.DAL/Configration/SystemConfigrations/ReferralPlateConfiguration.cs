using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class ReferralPlateConfiguration : BaseEntityLongConfiguration<ReferralPlate>
{
    public override void Configure(EntityTypeBuilder<ReferralPlate> builder)
    {
        base.Configure(builder);

        builder.ToTable("referral_plates");

        builder.Property(x => x.ReferralJobId).HasColumnName("referral_job_id").IsRequired();
        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

       
        builder.HasIndex(x => new { x.ReferralJobId, x.NormalizedPlate }).IsUnique();

        builder.HasIndex(x => new { x.ReferralJobId, x.Status });

        builder.HasOne(x => x.ReferralJob)
            .WithMany(x => x.ReferralPlates)
            .HasForeignKey(x => x.ReferralJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
