using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class RecordingArchivePlateConfiguration : BaseEntityLongConfiguration<RecordingArchivePlate>
{
    public override void Configure(EntityTypeBuilder<RecordingArchivePlate> builder)
    {
        base.Configure(builder);

        builder.ToTable("recording_archive_plates");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();
        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").HasMaxLength(100);
        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150);
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.RecorderName).HasColumnName("recorder_name").HasMaxLength(150);

        builder.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
        builder.Property(x => x.IsMatched).HasColumnName("is_matched").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.AppendedToDataFileId).HasColumnName("appended_to_data_file_id");
        builder.Property(x => x.AppendedAt).HasColumnName("appended_at");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.SourceSessionId);
        builder.HasIndex(x => x.NormalizedPlate);
        builder.HasIndex(x => new { x.UserId, x.AppendedToDataFileId });

        // عمدًا من غير navigation لـ RecordingSession/RecordedPlate — دول بيتمسحوا
        // عادي، ومفيش أي FK بينهم وبين الجدول ده.
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
