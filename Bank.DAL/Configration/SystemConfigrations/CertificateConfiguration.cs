using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class CertificateConfiguration : BaseEntityConfiguration<Certificate>
{
    public override void Configure(EntityTypeBuilder<Certificate> builder)
    {
        base.Configure(builder);
        builder.ToTable("certificates");

        builder.Property(x => x.DriveFileId).HasColumnName("drive_file_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(500).IsRequired();
        builder.Property(x => x.BatchNo).HasColumnName("batch_no").IsRequired();
        builder.Property(x => x.DiscoveredAt).HasColumnName("discovered_at").HasDefaultValueSql("now()").IsRequired();

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(CertificateProvider.Sijil)
            .IsRequired();
        builder.Property(x => x.DriveCreatedAt)
            .HasColumnName("drive_created_at")
            .IsRequired();
        builder.Property(x => x.PlateLetters).HasColumnName("plate_letters").HasMaxLength(10);
        builder.Property(x => x.PlateDigits).HasColumnName("plate_digits").HasMaxLength(10);
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30);
        builder.Property(x => x.Make).HasColumnName("make").HasMaxLength(100);
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(100);
        builder.Property(x => x.Color).HasColumnName("color").HasMaxLength(100);
        builder.Property(x => x.Lessor).HasColumnName("lessor").HasMaxLength(200);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(CertificateStatus.Pending)
            .IsRequired();
        builder.Property(x => x.StatusNote).HasColumnName("status_note").HasMaxLength(1000);
        builder.Property(x => x.StoragePath).HasColumnName("storage_path").HasMaxLength(500);
        builder.Property(x => x.IsManuallyEdited).HasColumnName("is_manually_edited").HasDefaultValue(false).IsRequired();

        // ---- بيانات Drive ----
        builder.Property(x => x.ParentFolderId).HasColumnName("parent_folder_id").HasMaxLength(100);
        builder.Property(x => x.DriveModifiedAt).HasColumnName("drive_modified_at");
        builder.Property(x => x.FileSize).HasColumnName("file_size");
        builder.Property(x => x.IsRemovedFromDrive).HasColumnName("is_removed_from_drive").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.RemovedFromDriveAt).HasColumnName("removed_from_drive_at");

        // ---- طابور الاستخراج ----
        builder.Property(x => x.ExtractionAttempts).HasColumnName("extraction_attempts").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ProcessingStartedAt).HasColumnName("processing_started_at");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.ExtractedAt).HasColumnName("extracted_at");

        // المفتاح اللي بيمنع التكرار: ملف Drive واحد = صف واحد (كل الـ inserts بـ ON CONFLICT عليه)
        builder.HasIndex(x => x.DriveFileId).IsUnique();
        builder.HasIndex(x => x.BatchNo);
        builder.HasIndex(x => x.NormalizedPlate);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.DiscoveredAt);
        builder.HasIndex(x => x.ParentFolderId);

        // البحث بجزء من الاسم / اللوحة (ILIKE '%...%') على ~400 ألف صف: GIN trigram بدل الـ sequential scan
        builder.HasIndex(x => x.FileName, "ix_certificates_file_name_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");
        builder.HasIndex(x => x.NormalizedPlate, "ix_certificates_normalized_plate_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        // طابور الاستخراج: الـ worker بيدوّر على Pending الأقدم بس (index جزئي صغير مهما كبر الجدول)
        builder.HasIndex(x => new { x.DiscoveredAt, x.NextAttemptAt }, "ix_certificates_extraction_queue")
            .HasFilter("status = 'Pending' AND is_removed_from_drive = false");
    }
}
