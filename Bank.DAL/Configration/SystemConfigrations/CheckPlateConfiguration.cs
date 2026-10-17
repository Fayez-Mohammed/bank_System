using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class CheckPlateConfiguration : BaseEntityLongConfiguration<CheckPlate>
{
    public override void Configure(EntityTypeBuilder<CheckPlate> builder)
    {
        base.Configure(builder);

        builder.ToTable("check_plates");

        builder.Property(x => x.CheckFileId).HasColumnName("check_file_id").IsRequired();
        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();
        builder.Property(x => x.BankName).HasColumnName("bank_name").HasMaxLength(150).IsRequired(true);
        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").HasMaxLength(1000);
        builder.Property(x => x.ChassisNumber).HasColumnName("chassis_number").HasMaxLength(100);
        builder.Property(x => x.ClaimStatus).HasColumnName("claim_status").HasMaxLength(100);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.NormalizedPlate);
        builder.HasIndex(x => new { x.CheckFileId, x.NormalizedPlate }).IsUnique();
       // builder.HasIndex(x => x.BankName);

        builder.HasOne(x => x.CheckFile)
            .WithMany(x => x.CheckPlates)
            .HasForeignKey(x => x.CheckFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
