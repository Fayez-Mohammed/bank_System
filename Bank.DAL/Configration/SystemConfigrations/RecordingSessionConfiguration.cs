using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class RecordingSessionConfiguration : BaseEntityConfiguration<RecordingSession>
{
    public override void Configure(EntityTypeBuilder<RecordingSession> builder)
    {
        base.Configure(builder);

        builder.ToTable("recording_sessions");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.GroupId).HasColumnName("group_id").IsRequired(false);
        builder.Property(x => x.CheckFileId).HasColumnName("check_file_id").IsRequired(false);

        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150).IsRequired();
        builder.Property(x => x.ExcelSheetName).HasColumnName("excel_sheet_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.RecorderName).HasColumnName("recorder_name");
        builder.Property(x => x.IsRecord).HasColumnName("is_record").HasDefaultValue(true);
        builder.Property(x => x.TrackingMode)
            .HasColumnName("tracking_mode")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(TrackingMode.Auto)
            .IsRequired();

        builder.Property(x => x.TrackingIntervalSeconds).HasColumnName("tracking_interval_seconds").HasDefaultValue(3);
        builder.Property(x => x.TotalGpsPoints).HasColumnName("total_gps_points").HasDefaultValue(0);
        builder.Property(x => x.TotalPlates).HasColumnName("total_plates").HasDefaultValue(0);
        builder.Property(x => x.AudioUrl).HasColumnName("audio_url").HasColumnType("text");
        builder.Property(x => x.ProcessingError).HasColumnName("processing_error").HasColumnType("text");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(SessionStatus.Pending)
            .IsRequired();

        builder.Property(x => x.StartedAt).HasColumnName("started_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");

        builder.HasIndex(x => new { x.UserId, x.StartedAt });
        builder.HasIndex(x => new { x.GroupId, x.StartedAt });

        //// بتضمن إن اسم العرض ما يتكررش جوه نفس الـ scope — الاعتماد الوحيد
        //// على "دور على الاسم في الميموري وبعدين Insert" فيه race condition؛
        //// الـ index ده شبكة الأمان الحقيقية (نعمل retry لو حصل Duplicate Key).
        //builder.HasIndex(x => new { x.GroupId, x.DisplayName })
        //    .HasDatabaseName("UX_RecordingSessions_DisplayName_PerGroup")
        //    .IsUnique()
        //    .HasFilter("group_id IS NOT NULL");

        //builder.HasIndex(x => new { x.UserId, x.DisplayName })
        //    .HasDatabaseName("UX_RecordingSessions_DisplayName_PerSoloUser")
        //    .IsUnique()
        //    .HasFilter("group_id IS NULL");
        builder.HasIndex(s => new { s.UserId, s.DisplayName })
    .IsUnique()
    .HasDatabaseName("UX_RecordingSessions_DisplayName_PerUser");
        builder.HasOne(x => x.Group)
            .WithMany(x => x.RecordingSessions)
            .HasForeignKey(x => x.GroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.User)
            .WithMany(x => x.RecordingSessions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // CheckFile ممكن يتحذف بعدين (تاريخيًا) وده مايأثرش على تاريخ التسجيل
        builder.HasOne(x => x.CheckFile)
            .WithMany()
            .HasForeignKey(x => x.CheckFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
