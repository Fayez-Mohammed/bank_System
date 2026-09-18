using Bank.DAL.Configration.BaseConfigrations;
using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class MatchResultConfiguration : BaseEntityLongConfiguration<MatchResult>
{
    public override void Configure(EntityTypeBuilder<MatchResult> builder)
    {
        base.Configure(builder);

        builder.ToTable("match_results");

        builder.Property(x => x.ReferralJobId).HasColumnName("referral_job_id").IsRequired();
        builder.Property(x => x.HistoricalPlateId).HasColumnName("historical_plate_id").IsRequired();
        builder.Property(x => x.NormalizedPlate).HasColumnName("normalized_plate").HasMaxLength(30).IsRequired();
        builder.Property(x => x.FoundVehicleType).HasColumnName("found_vehicle_type").HasMaxLength(100);
        builder.Property(x => x.FoundDistrict).HasColumnName("found_district").HasMaxLength(150);
        builder.Property(x => x.FoundStreet).HasColumnName("found_street").HasMaxLength(150);
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        builder.Property(x => x.FoundRecordedAt).HasColumnName("found_recorded_at").HasMaxLength(50);
        builder.Property(x => x.SourceDataFileId).HasColumnName("source_data_file_id").IsRequired();
        builder.Property(x => x.MatchedAt).HasColumnName("matched_at").HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

        builder.HasIndex(x => new { x.ReferralJobId, x.NormalizedPlate });
        builder.HasIndex(x => x.SourceDataFileId);
        builder.HasIndex(x => x.HistoricalPlateId);

        builder.HasOne(x => x.ReferralJob)
            .WithMany(x => x.MatchResults)
            .HasForeignKey(x => x.ReferralJobId)
            .OnDelete(DeleteBehavior.Cascade);

       
        builder.HasOne(x => x.HistoricalPlate)
            .WithMany(x => x.MatchResults)
            .HasForeignKey(x => x.HistoricalPlateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SourceDataFile)
            .WithMany(x => x.MatchResultsAsSource)
            .HasForeignKey(x => x.SourceDataFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
