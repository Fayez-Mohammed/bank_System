using Bank.DAL.Models.SystemModels;
using Bank.Shared.Enums;
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

        builder.Property(x => x.BaselineStatus).HasColumnName("baseline_status").HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(CertificateBaselineStatus.NotStarted).IsRequired();
        builder.Property(x => x.BaselineStartedAt).HasColumnName("baseline_started_at");
        builder.Property(x => x.BaselineCompletedAt).HasColumnName("baseline_completed_at");
        builder.Property(x => x.BaselinePageToken).HasColumnName("baseline_page_token").HasMaxLength(2000);
        builder.Property(x => x.BaselinePagesDone).HasColumnName("baseline_pages_done");
        builder.Property(x => x.BaselineFilesSeen).HasColumnName("baseline_files_seen");
        builder.Property(x => x.BaselineIndexedHistorical).HasColumnName("baseline_indexed_historical");
        builder.Property(x => x.BaselineQueuedNew).HasColumnName("baseline_queued_new");

        builder.Property(x => x.ChangesPageToken).HasColumnName("changes_page_token").HasMaxLength(2000);
        builder.Property(x => x.ScanWatermark).HasColumnName("scan_watermark");

        builder.Property(x => x.LastSyncStartedAt).HasColumnName("last_sync_started_at");
        builder.Property(x => x.LastSyncCompletedAt).HasColumnName("last_sync_completed_at");
        builder.Property(x => x.LastSyncKind).HasColumnName("last_sync_kind").HasMaxLength(20);
        builder.Property(x => x.LastSyncFilesListed).HasColumnName("last_sync_files_listed");
        builder.Property(x => x.LastSyncAlreadyIndexed).HasColumnName("last_sync_already_indexed");
        builder.Property(x => x.LastSyncIndexedHistorical).HasColumnName("last_sync_indexed_historical");
        builder.Property(x => x.LastSyncNewFiles).HasColumnName("last_sync_new_files");
        builder.Property(x => x.LastSyncRemoved).HasColumnName("last_sync_removed");
        builder.Property(x => x.LastSyncDriveRequests).HasColumnName("last_sync_drive_requests");
        builder.Property(x => x.LastSyncDurationMs).HasColumnName("last_sync_duration_ms");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);

        builder.Property(x => x.LastReconcileAt).HasColumnName("last_reconcile_at");
        builder.Property(x => x.LastReconcileMarkedDeleted).HasColumnName("last_reconcile_marked_deleted");
        builder.Property(x => x.TotalDriveRequests).HasColumnName("total_drive_requests");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
