using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class RecordingGpsPointConfiguration : BaseEntityLongConfiguration<RecordingGpsPoint>
{
    public override void Configure(EntityTypeBuilder<RecordingGpsPoint> builder)
    {
        base.Configure(builder);

        builder.ToTable("recording_gps_points");

        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.Location).HasColumnName("location").HasColumnType("geography").IsRequired();
        builder.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
        builder.Property(x => x.AccuracyMeters).HasColumnName("accuracy_meters");
        builder.Property(x => x.IsManual).HasColumnName("is_manual").HasDefaultValue(false).IsRequired();

        // أهم index في الجدول ده — أساس عملية "أقرب توقيت" بين اللوحة والـ GPS
        builder.HasIndex(x => new { x.SessionId, x.CapturedAt });

        builder.HasOne(x => x.Session)
            .WithMany(x => x.GpsPoints)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
