using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class UserDataFilePermissionConfiguration : BaseEntityConfiguration<UserDataFilePermission>
{
    public override void Configure(EntityTypeBuilder<UserDataFilePermission> builder)
    {
        base.Configure(builder);

        builder.ToTable("user_data_file_permissions");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.DataFileId).HasColumnName("data_file_id").IsRequired();
        builder.Property(x => x.GrantedByAdminId).HasColumnName("granted_by_admin_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // إذن واحد بس لكل (مستخدم، ملف)
        builder.HasIndex(x => new { x.UserId, x.DataFileId }).IsUnique();
        builder.HasIndex(x => x.DataFileId);
        builder.HasIndex(x => x.GrantedByAdminId);

        builder.HasOne(x => x.User)
            .WithMany(u => u.DataFilePermissions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // حذف ملف الداتا بيشيل الأذونات بتاعته
        builder.HasOne(x => x.DataFile)
            .WithMany(f => f.UserPermissions)
            .HasForeignKey(x => x.DataFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.GrantedByAdmin)
            .WithMany()
            .HasForeignKey(x => x.GrantedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
