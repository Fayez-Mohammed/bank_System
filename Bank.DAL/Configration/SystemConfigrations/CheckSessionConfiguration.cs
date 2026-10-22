using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bank.DAL.Configration.SystemConfigrations;

public class CheckSessionConfiguration : BaseEntityConfiguration<CheckSession>
{
    public override void Configure(EntityTypeBuilder<CheckSession> builder)
    {
        base.Configure(builder);

        builder.ToTable("check_sessions");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.GroupId).HasColumnName("group_id");
        builder.Property(x => x.CheckFileId).HasColumnName("check_file_id");

        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150).IsRequired();
        builder.Property(x => x.ExcelSheetName).HasColumnName("excel_sheet_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.RecorderName).HasColumnName("recorder_name").HasMaxLength(200).IsRequired();
    

        builder.Property(x => x.TotalPlates).HasColumnName("total_plates").HasDefaultValue(0);
        builder.Property(x => x.ProcessingError).HasColumnName("processing_error").HasColumnType("text");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(SessionStatus.Recording)
            .IsRequired();

        builder.Property(x => x.StartedAt).HasColumnName("started_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");

        builder.HasIndex(x => new { x.UserId, x.StartedAt });
        builder.HasIndex(x => new { x.GroupId, x.StartedAt });

        builder.HasIndex(x => new { x.GroupId, x.DisplayName })
            .HasDatabaseName("UX_CheckSessions_DisplayName_PerGroup")
            .IsUnique()
            .HasFilter("group_id IS NOT NULL");

        builder.HasIndex(x => new { x.UserId, x.DisplayName })
            .HasDatabaseName("UX_CheckSessions_DisplayName_PerSoloUser")
            .IsUnique()
            .HasFilter("group_id IS NULL");

        builder.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CheckFile).WithMany().HasForeignKey(x => x.CheckFileId).OnDelete(DeleteBehavior.SetNull);
    }
}
