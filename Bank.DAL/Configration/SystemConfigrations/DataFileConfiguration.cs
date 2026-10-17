using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class DataFileConfiguration : BaseEntityConfiguration<DataFile>
{
    public override void Configure(EntityTypeBuilder<DataFile> builder)
    {
        base.Configure(builder);

        builder.ToTable("data_files");

        builder.Property(x => x.GroupId)
            .HasColumnName("group_id")
            .IsRequired(false);

        builder.Property(x => x.OwnerId)
            .HasColumnName("owner_id")
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.RowCount)
            .HasColumnName("row_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.IsActiveForFiltering)
            .HasColumnName("is_active_for_filtering")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(255);

        builder.Property(x => x.SortOrder)
            .HasColumnName("sort_order")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.StoragePath)
            .HasColumnName("storage_path")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.ImportStatus)
            .HasColumnName("import_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(DataFileImportStatus.Pending)
            .IsRequired();

        builder.Property(x => x.ImportError)
            .HasColumnName("import_error")
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(x => x.GroupId);
        builder.HasIndex(x => new { x.GroupId, x.IsActiveForFiltering });
        builder.HasIndex(x => new { x.GroupId, x.SortOrder });

        builder.HasIndex(x => x.OwnerId);
        builder.HasIndex(x => new { x.OwnerId, x.IsActiveForFiltering });
        builder.HasIndex(x => new { x.OwnerId, x.SortOrder });

        // Relationships
        builder.HasOne(x => x.Group)
            .WithMany(x => x.DataFiles)
            .HasForeignKey(x => x.GroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Owner)
            .WithMany(x => x.OwnedDataFiles)
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}