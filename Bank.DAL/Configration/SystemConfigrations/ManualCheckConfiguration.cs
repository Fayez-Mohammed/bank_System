using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class ManualCheckConfiguration : BaseEntityLongConfiguration<ManualCheck>
{
    public override void Configure(EntityTypeBuilder<ManualCheck> builder)
    {
        builder.ToTable("manual_checks");


        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.RawPlate)
            .HasColumnName("raw_plate")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.NormalizedPlate)
            .HasColumnName("normalized_plate")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.VehicleType)
            .HasColumnName("vehicle_type")
            .HasMaxLength(100);

        builder.Property(x => x.CheckFileId)
            .HasColumnName("check_file_id");

        builder.Property(x => x.IsMatched)
            .HasColumnName("is_matched")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.MatchedCheckPlateId)
            .HasColumnName("matched_check_plate_id");

        builder.Property(x => x.Location)
            .HasColumnName("location")
            .HasColumnType("geography");

        builder.Property(x => x.CheckedAt)
            .HasColumnName("checked_at")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .IsRequired();



        builder.HasIndex(x => x.NormalizedPlate);

        builder.HasIndex(x => new
        {
            x.UserId,
            x.CheckedAt
        });

        builder.HasIndex(x => x.CheckFileId);

        builder.HasIndex(x => new
        {
            x.UserId,
            x.IsMatched
        });

       

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CheckFile)
            .WithMany()
            .HasForeignKey(x => x.CheckFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.MatchedCheckPlate)
            .WithMany()
            .HasForeignKey(x => x.MatchedCheckPlateId)
            .OnDelete(DeleteBehavior.NoAction);

        
    }
}