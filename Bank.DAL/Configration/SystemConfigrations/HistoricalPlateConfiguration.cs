using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class HistoricalPlateConfiguration : BaseEntityLongConfiguration<HistoricalPlate>
{
    public override void Configure(EntityTypeBuilder<HistoricalPlate> builder)
    {
        base.Configure(builder);

        builder.ToTable("historical_plates");

        builder.Property(x => x.DataFileId).HasColumnName("data_file_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();
        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").HasMaxLength(100);
        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150);
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150);
        builder.Property(x => x.Location)
            .HasColumnName("location")
            .HasColumnType("nvarchar(500)")
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("nvarchar(500)")
            .HasMaxLength(500)
            .IsRequired(false); ;

        builder.HasIndex(x => x.NormalizedPlate);
        builder.HasIndex(x => new { x.DataFileId, x.NormalizedPlate });
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.DataFile)
            .WithMany(x => x.HistoricalPlates)
            .HasForeignKey(x => x.DataFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}