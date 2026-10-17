using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class CheckFileConfiguration : BaseEntityConfiguration<CheckFile>
{
    public override void Configure(EntityTypeBuilder<CheckFile> builder)
    {
        base.Configure(builder);

        builder.ToTable("check_files");

        builder.Property(x => x.GroupId).HasColumnName("group_id");
        builder.Property(x => x.UploadedBy).HasColumnName("uploaded_by").IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.TotalPlates).HasColumnName("total_plates").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.UploadedBy);

        builder.HasIndex(x => x.GroupId)
            .HasDatabaseName("UX_CheckFiles_OneActivePerGroup")
            .IsUnique()
            .HasFilter("is_active = true AND group_id IS NOT NULL");

        builder.HasIndex(x => x.UploadedBy)
            .HasDatabaseName("UX_CheckFiles_OneActivePerSoloUser")
            .IsUnique()
            .HasFilter("is_active = true AND group_id IS NULL");

        builder.HasOne(x => x.Group)
            .WithMany(x => x.CheckFiles)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Uploader)
            .WithMany(x => x.UploadedCheckFiles)
            .HasForeignKey(x => x.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
