using Bank.DAL.Models.BaseModels;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.BaseConfigrations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UserName)
            .HasColumnName("username")
            .HasMaxLength(100);

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash");

        builder.Property(x => x.GroupId)
            .HasColumnName("group_id")
            .IsRequired(false);
        builder.Property(x => x.BoundDeviceHash).HasColumnName("bound_device_hash").HasMaxLength(64);
        builder.Property(x => x.BoundDeviceName).HasColumnName("bound_device_name").HasMaxLength(150);
        builder.Property(x => x.DeviceBoundAt).HasColumnName("device_bound_at");
        builder.HasIndex(x => x.GroupId);
        builder.HasOne(x => x.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.UserPermission)
            .WithOne(p => p.User)
            .HasForeignKey<UserPermission>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.RefreshTokens)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AdministeredGroup)
            .WithOne(g => g.Admin)
            .HasForeignKey<Group>(g => g.AdminId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}