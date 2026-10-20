using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class UserPermissionConfiguration : BaseEntityConfiguration<UserPermission>
{
    public override void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        base.Configure(builder);

        builder.ToTable("user_permissions");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(x => x.CanManageCheckFiles)
            .HasColumnName("can_manage_check_files")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.CanUploadDataFiles)
            .HasColumnName("can_upload_data_files")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.CanSort)
            .HasColumnName("can_sort")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // كل يوزر له سجل صلاحيات واحد فقط
        builder.HasIndex(x => x.UserId).IsUnique();

        builder.HasOne(x => x.User)
            .WithOne(u => u.UserPermission)
            .HasForeignKey<UserPermission>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
