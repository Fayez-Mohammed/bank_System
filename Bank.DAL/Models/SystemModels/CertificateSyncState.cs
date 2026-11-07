using Bank.Shared.Enums;

namespace Bank.DAL.Models.SystemModels;

/// <summary>
/// صف واحد بس (Id = 1): حالة مزامنة Drive. بيخلّي الـ Baseline قابل للاستكمال بعد أي انقطاع،
/// وبيحتفظ بـ cursor الـ Changes API وبأرقام آخر مزامنة عشان الـ status endpoint ماتكونش تخمين.
/// </summary>
public class CertificateSyncState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    // ---------- Baseline (الفهرسة الأولية للأرشيف) ----------
    public CertificateBaselineStatus BaselineStatus { get; set; } = CertificateBaselineStatus.NotStarted;
    /// <summary>وقت بداية الـ Baseline: أي ملف createdTime بتاعه بعده = شهادة جديدة بتتستخرج.</summary>
    public DateTime? BaselineStartedAt { get; set; }
    public DateTime? BaselineCompletedAt { get; set; }
    /// <summary>nextPageToken لقايمة Drive عشان الاستكمال من آخر صفحة.</summary>
    public string? BaselinePageToken { get; set; }
    public int BaselinePagesDone { get; set; }
    public int BaselineFilesSeen { get; set; }
    public int BaselineIndexedHistorical { get; set; }
    public int BaselineQueuedNew { get; set; }

    // ---------- Incremental ----------
    /// <summary>cursor الـ Changes API (rename / move / trash / ملفات اتضافت).</summary>
    public string? ChangesPageToken { get; set; }
    /// <summary>الـ Scan بيدوّر على createdTime بعد العلامة دي (ناقص overlap).</summary>
    public DateTime? ScanWatermark { get; set; }

    // ---------- آخر مزامنة ----------
    public DateTime? LastSyncStartedAt { get; set; }
    public DateTime? LastSyncCompletedAt { get; set; }
    public string? LastSyncKind { get; set; }            // Baseline | Incremental | Reconcile
    public int LastSyncFilesListed { get; set; }
    public int LastSyncAlreadyIndexed { get; set; }
    public int LastSyncIndexedHistorical { get; set; }
    public int LastSyncNewFiles { get; set; }
    public int LastSyncRemoved { get; set; }
    public int LastSyncDriveRequests { get; set; }
    public int LastSyncDurationMs { get; set; }
    public string? LastError { get; set; }

    public DateTime? LastReconcileAt { get; set; }
    public int LastReconcileMarkedDeleted { get; set; }

    public long TotalDriveRequests { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
