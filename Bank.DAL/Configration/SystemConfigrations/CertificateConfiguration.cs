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

        // ---- فهرس Drive ----
        builder.Property(x => x.Origin)
            .HasColumnName("origin")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(CertificateOrigin.New)   // الصفوف القديمة كلها عدّت على خط الاستخراج = New
            .IsRequired();
        builder.Property(x => x.ParentFolderId).HasColumnName("parent_folder_id").HasMaxLength(100);
        builder.Property(x => x.ParentFolderName).HasColumnName("parent_folder_name").HasMaxLength(300);
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        builder.Property(x => x.DriveModifiedAt).HasColumnName("drive_modified_at");
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(x => x.IsDeletedInDrive).HasColumnName("is_deleted_in_drive").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedInDriveAt).HasColumnName("deleted_in_drive_at");

        // ---- خط الاستخراج ----
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ProcessingStartedAt).HasColumnName("processing_started_at");
        builder.Property(x => x.AvailableAt).HasColumnName("available_at");
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.ExtractionDurationMs).HasColumnName("extraction_duration_ms");

        builder.HasIndex(x => x.DriveFileId).IsUnique();   // ضمان عدم التكرار حتى مع مزامنات متزامنة (INSERT .. ON CONFLICT)
        builder.HasIndex(x => x.BatchNo);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.DiscoveredAt);
        builder.HasIndex(x => x.ParentFolderId);

        // البحث بالاسم / اللوحة بـ ILIKE '%term%': محتاج GIN + pg_trgm (btree ما بيخدمش LIKE '%..%').
        builder.HasIndex(x => x.FileName)
            .HasDatabaseName("IX_certificates_file_name_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");
        builder.HasIndex(x => x.NormalizedPlate)
            .HasDatabaseName("IX_certificates_normalized_plate_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        // الـ queue بتاع الـ worker: partial index صغير على الـ Pending بس، بترتيب الحجز.
        builder.HasIndex(x => new { x.BatchNo, x.DriveCreatedAt })
            .HasDatabaseName("IX_certificates_pending_queue")
            .HasFilter("status = 'Pending'");
    }
}
