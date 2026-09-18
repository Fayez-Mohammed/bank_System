using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class ReferralJobConfiguration : BaseEntityConfiguration<ReferralJob>
{
    public override void Configure(EntityTypeBuilder<ReferralJob> builder)
    {
        base.Configure(builder);

        builder.ToTable("referral_jobs");

        builder.Property(x => x.GroupId).HasColumnName("group_id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(x => x.FilterType)
            .HasColumnName("filter_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.SourceFileName).HasColumnName("source_file_name").HasMaxLength(255);
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255);

        builder.Property(x => x.TotalReferralPlates).HasColumnName("total_referral_plates").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.SkippedPlatesCount).HasColumnName("skipped_plates_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MatchedPlatesCount).HasColumnName("matched_plates_count").HasDefaultValue(0).IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(JobStatus.Processing)
            .IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

        builder.HasIndex(x => new { x.GroupId, x.CreatedAt });
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });

        builder.HasOne(x => x.Group)
            .WithMany(x => x.ReferralJobs)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.User)
            .WithMany(x => x.ReferralJobs)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
