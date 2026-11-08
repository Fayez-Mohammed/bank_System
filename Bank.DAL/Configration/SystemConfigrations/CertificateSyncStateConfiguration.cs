using Bank.DAL.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.DAL.Configration.SystemConfigrations;

public class CertificateSyncStateConfiguration : IEntityTypeConfiguration<CertificateSyncState>
{
    public void Configure(EntityTypeBuilder<CertificateSyncState> builder)
    {
        builder.ToTable("certificate_sync_state");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.BaselinePhase).HasColumnName("baseline_phase").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.BaselinePageToken).HasColumnName("baseline_page_token").HasMaxLength(2000);
        builder.Property(x => x.BaselineStartedAt).HasColumnName("baseline_started_at");
        builder.Property(x => x.BaselineCompletedAt).HasColumnName("baseline_completed_at");
        builder.Property(x => x.BaselineNodesListed).HasColumnName("baseline_nodes_listed");
        builder.Property(x => x.BaselineFilesListed).HasColumnName("baseline_files_listed");
        builder.Property(x => x.BaselineFilesInScope).HasColumnName("baseline_files_in_scope");
        builder.Property(x => x.ExtractCutoff).HasColumnName("extract_cutoff");
        builder.Property(x => x.ChangesPageToken).HasColumnName("changes_page_token").HasMaxLength(2000);
        builder.Property(x => x.LastChangesSyncAt).HasColumnName("last_changes_sync_at");
        builder.Property(x => x.ReconcileWatermark).HasColumnName("reconcile_watermark");
        builder.Property(x => x.LastRunStartedAt).HasColumnName("last_run_started_at");
        builder.Property(x => x.LastRunFinishedAt).HasColumnName("last_run_finished_at");
        builder.Property(x => x.LastRunStatsJson).HasColumnName("last_run_stats_json");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(x => x.LastErrorAt).HasColumnName("last_error_at");
        builder.Property(x => x.ConsecutiveFailures).HasColumnName("consecutive_failures");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
    }
}
