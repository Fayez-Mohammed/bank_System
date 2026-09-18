using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class AuditLogConfiguration : BaseEntityLongConfiguration<AuditLog>
{
    public override void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        base.Configure(builder);

        builder.ToTable("audit_logs");

        // Nullable for system background events
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.GroupId).HasColumnName("group_id");

        builder.Property(x => x.ActionType)
            .HasColumnName("action_type")
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EntityName).HasColumnName("entity_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.OldValues).HasColumnName("old_values").HasColumnType("nvarchar(max)");
        builder.Property(x => x.NewValues).HasColumnName("new_values").HasColumnType("nvarchar(max)");
        builder.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

        builder.HasIndex(x => new { x.GroupId, x.CreatedAt });
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => new { x.EntityName, x.EntityId });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}