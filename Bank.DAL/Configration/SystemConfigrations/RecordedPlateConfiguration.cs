using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class RecordedPlateConfiguration : BaseEntityLongConfiguration<RecordedPlate>
{
    public override void Configure(EntityTypeBuilder<RecordedPlate> builder)
    {
        base.Configure(builder);

        builder.ToTable("recorded_plates");

        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.SequenceNumber).HasColumnName("sequence_number").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();
        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").HasMaxLength(100);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(255);
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150);
        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150);
        builder.Property(x => x.Location).HasColumnName("location").HasColumnType("geography");
        builder.Property(x => x.AudioOffsetMs).HasColumnName("audio_offset_ms");
        builder.Property(x => x.IsMatched).HasColumnName("is_matched").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.MatchedCheckPlateId).HasColumnName("matched_check_plate_id");
        builder.Property(x => x.IsManualEntry).HasColumnName("is_manual_entry").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

        builder.HasIndex(x => x.NormalizedPlate);
        builder.HasIndex(x => new { x.SessionId, x.IsMatched });
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.Session)
            .WithMany(x => x.RecordedPlates)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.MatchedCheckPlate)
            .WithMany(x => x.MatchedRecordedPlates)
            .HasForeignKey(x => x.MatchedCheckPlateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
