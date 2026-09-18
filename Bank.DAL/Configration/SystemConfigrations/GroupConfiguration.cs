using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class GroupConfiguration : BaseEntityConfiguration<Group>
{
    public override void Configure(EntityTypeBuilder<Group> builder)
    {
        base.Configure(builder);

        builder.ToTable("groups");

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.AdminId)
            .HasColumnName("admin_id")
            .IsRequired(false);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.ShareAdminPins)
            .HasColumnName("share_admin_pins")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.AdminPinsSharedAt)
            .HasColumnName("admin_pins_shared_at");

        builder.Property(x => x.LastSharedReferralJobId)
            .HasColumnName("last_shared_referral_job_id");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .IsRequired();

        builder.HasIndex(x => x.AdminId)
            .IsUnique()
            .HasFilter("[admin_id] IS NOT NULL");

        builder.HasIndex(x => x.LastSharedReferralJobId);

        builder.HasOne(g => g.Admin)
            .WithOne(u => u.AdministeredGroup)
            .HasForeignKey<Group>(g => g.AdminId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Members)
            .WithOne(u => u.Group)
            .HasForeignKey(u => u.GroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.LastSharedReferralJob)
            .WithMany()
            .HasForeignKey(x => x.LastSharedReferralJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}