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

        builder.HasIndex(x => x.DriveFileId).IsUnique();
        builder.HasIndex(x => x.BatchNo);
        builder.HasIndex(x => x.NormalizedPlate);
        builder.HasIndex(x => x.Status);
    }
}
