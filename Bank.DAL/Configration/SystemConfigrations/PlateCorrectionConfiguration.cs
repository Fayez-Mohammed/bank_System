using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class PlateCorrectionConfiguration : BaseEntityLongConfiguration<PlateCorrection>
{
    public override void Configure(EntityTypeBuilder<PlateCorrection> builder)
    {
        base.Configure(builder);

        builder.ToTable("plate_corrections");

        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.SessionType).HasColumnName("session_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PlateId).HasColumnName("plate_id");
        builder.Property(x => x.ArchiveRowId).HasColumnName("archive_row_id");
        builder.Property(x => x.IsManualPlate).HasColumnName("is_manual_plate").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.OldPlate).HasColumnName("old_plate").HasMaxLength(50);
        builder.Property(x => x.NewPlate).HasColumnName("new_plate").HasMaxLength(50);
        builder.Property(x => x.OldVehicleType).HasColumnName("old_vehicle_type").HasMaxLength(100);
        builder.Property(x => x.NewVehicleType).HasColumnName("new_vehicle_type").HasMaxLength(100);
        builder.Property(x => x.OldNotes).HasColumnName("old_notes").HasMaxLength(500);
        builder.Property(x => x.NewNotes).HasColumnName("new_notes").HasMaxLength(500);
        builder.Property(x => x.AudioOffsetMs).HasColumnName("audio_offset_ms");
        builder.Property(x => x.AudioStartMs).HasColumnName("audio_start_ms");
        builder.Property(x => x.AudioEndMs).HasColumnName("audio_end_ms");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.RecorderName).HasColumnName("recorder_name").HasMaxLength(200);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.SessionId);
    }
}
