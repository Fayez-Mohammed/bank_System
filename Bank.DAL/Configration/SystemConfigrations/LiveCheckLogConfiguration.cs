using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class LiveCheckLogConfiguration : BaseEntityLongConfiguration<LiveCheckLog>
{
    public override void Configure(EntityTypeBuilder<LiveCheckLog> builder)
    {
        base.Configure(builder);

        builder.ToTable("live_check_logs");

        builder.Property(x => x.CheckSessionId).HasColumnName("check_session_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.GroupId).HasColumnName("group_id");
        builder.Property(x => x.RawPlate).HasColumnName("raw_plate").HasMaxLength(50).IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();

        builder.Property(x => x.CheckType)
            .HasColumnName("check_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(CheckType.Live)
            .IsRequired();

        builder.Property(x => x.VehicleType).HasColumnName("vehicle_type").HasMaxLength(100);
        builder.Property(x => x.District).HasColumnName("district").HasMaxLength(150);
        builder.Property(x => x.Street).HasColumnName("street").HasMaxLength(150);
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        builder.Property(x => x.IsMatched).HasColumnName("is_matched").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.MatchedCheckPlateId).HasColumnName("matched_check_plate_id");
        builder.Property(x => x.CheckedAt).HasColumnName("checked_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.NormalizedPlate);
        builder.HasIndex(x => new { x.CheckSessionId, x.CheckedAt });
        builder.HasIndex(x => new { x.UserId, x.CheckedAt });
        builder.HasIndex(x => x.IsMatched);

        builder.HasOne(x => x.CheckSession)
            .WithMany(x => x.Logs)
            .HasForeignKey(x => x.CheckSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User)
     .WithMany(x => x.LiveCheckLogs)
     .HasForeignKey(x => x.UserId)
     .OnDelete(DeleteBehavior.Restrict);
        // builder.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Group)
     .WithMany(x => x.LiveCheckLogs)
     .HasForeignKey(x => x.GroupId)
     .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.MatchedCheckPlate)
            .WithMany(x => x.MatchedLiveCheckLogs)
            .HasForeignKey(x => x.MatchedCheckPlateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
