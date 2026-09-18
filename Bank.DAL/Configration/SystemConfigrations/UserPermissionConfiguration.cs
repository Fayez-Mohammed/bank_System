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
      //  builder.Property(x => x.GroupId).HasColumnName("group_id").IsRequired(false);

        //builder.Property(x => x.CanFilter)
        //    .HasColumnName("can_filter")
        //    .HasDefaultValue(true)
        //    .IsRequired();

        //builder.Property(x => x.CanAppendToTeamFiles)
        //    .HasColumnName("can_append_to_team_files")
        //    .HasDefaultValue(false)
        //    .IsRequired();

        builder.Property(x => x.CanDownload)
            .HasColumnName("can_download")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.CanShare)
            .HasColumnName("can_share")
            .HasDefaultValue(false)
            .IsRequired();

        //builder.Property(x => x.CanManageAdminPins)
        //    .HasColumnName("can_manage_admin_pins")
        //    .HasDefaultValue(false)
        //    .IsRequired();

        //builder.Property(x => x.CanToggleFiles)
        //    .HasColumnName("can_toggle_files")
        //    .HasDefaultValue(false)
        //    .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .IsRequired();
        builder.Property(x => x.CanUploadCheckFile).HasColumnName("can_upload_check_file").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CanUploadDataFiles).HasColumnName("can_upload_data_files").HasDefaultValue(false).IsRequired();
        //builder.Property(x => x.CanRecord).HasColumnName("can_record").HasDefaultValue(false).IsRequired();
        //builder.Property(x => x.CanLiveCheck).HasColumnName("can_live_check").HasDefaultValue(false).IsRequired();
        //builder.Property(x => x.CanManualCheck).HasColumnName("can_manual_check").HasDefaultValue(false).IsRequired();
        // كل يوزر له سجل صلاحيات واحد فقط
      //  builder.HasIndex(x => x.UserId).IsUnique();
        //builder.HasIndex(x => x.GroupId);

        //builder.HasOne(x => x.User)
        //    .WithMany(x => x.UserPermissions)
        //    .HasForeignKey(x => x.UserId)
        //    .OnDelete(DeleteBehavior.Cascade);

        //builder.HasOne(x => x.Group)
        //    .WithMany(x => x.UserPermissions)
        //    .HasForeignKey(x => x.GroupId)
        //    .IsRequired(false)
        //    .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.UserId)
    .IsUnique();

        builder.HasOne(x => x.User)
            .WithOne(u => u.UserPermission) // أو WithOne() إن لم تكن الخاصية موجودة في User
            .HasForeignKey<UserPermission>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
