using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class CertificateDriveNodeConfiguration : IEntityTypeConfiguration<CertificateDriveNode>
{
    public void Configure(EntityTypeBuilder<CertificateDriveNode> builder)
    {
        builder.ToTable("certificate_drive_nodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasMaxLength(100).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ParentId).HasColumnName("parent_id").HasMaxLength(100);
        builder.Property(x => x.ShortcutTargetId).HasColumnName("shortcut_target_id").HasMaxLength(100);
        builder.Property(x => x.IsSharedRoot).HasColumnName("is_shared_root").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.InScope).HasColumnName("in_scope").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.NeedsScan).HasColumnName("needs_scan").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsRemoved).HasColumnName("is_removed").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => x.ShortcutTargetId);
        builder.HasIndex(x => x.IsSharedRoot).HasFilter("is_shared_root = true");
        builder.HasIndex(x => x.NeedsScan).HasFilter("needs_scan = true");
    }
}
