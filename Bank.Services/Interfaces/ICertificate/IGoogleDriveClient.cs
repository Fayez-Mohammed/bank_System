namespace Bank.Services.Interfaces.ICertificate;

/// <param name="ParentId">أول parent (المجلد المباشر). null لو مش معروف.</param>
public record DriveFileInfo(
    string FileId,
    string Name,
    DateTime? CreatedAt,
    DateTime? ModifiedAt = null,
    string? ParentId = null,
    long? Size = null,
    bool Trashed = false);

/// <summary>صفحة واحدة من قايمة PDFs. NextPageToken = null يعني آخر صفحة.</summary>
public record DrivePage(List<DriveFileInfo> Files, string? NextPageToken, int RawItems);

/// <param name="File">PDF بس (null لو التغيير على مجلد/نوع تاني أو الملف اتشال).</param>
public record DriveChange(string FileId, bool Removed, DriveFileInfo? File);

public record DriveChangesPage(List<DriveChange> Changes, string? NextPageToken, string? NewStartPageToken);

public record DriveProbeResult(
    string? AccountEmail,
    bool ChangesTokenOk,
    int FlatPdfFirstPageCount,
    bool FlatPdfHasMorePages,
    int SharedWithMeTopLevelCount,
    List<string> SampleFileNames);

/// <summary>NotConfigured | Unknown | Ok | NeedsReauth. بيتحفظ في الذاكرة بس (مش في الـ DB).</summary>
public record DriveStateSnapshot(
    string State, string? AccountEmail, DateTime? LastSyncAt, int LastSyncNewFiles, string? LastError);

/// <summary>Drive مش شغال: إعدادات ناقصة، أو الـ Refresh Token اتلغى/انتهى.</summary>
public class DriveAuthException : Exception
{
    public DriveAuthException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>جوجل بترفض بسبب quota/rate limit حتى بعد إعادة المحاولة بـ backoff. الـ worker بيوقف مؤقتاً.</summary>
public class DriveThrottledException : Exception
{
    public DriveThrottledException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>الملف مش موجود/مش متاح في Drive (اتمسح أو اتسحب الوصول).</summary>
public class DriveFileNotFoundException : Exception
{
    public DriveFileNotFoundException(string fileId, Exception? inner = null)
        : base($"الملف {fileId} مش موجود في Drive.", inner) { }
}

/// <summary>
/// Drive واحد global للنظام كله. الإعدادات (ClientId / ClientSecret / RefreshToken) من الـ configuration مرة واحدة.
/// الاكتشاف بقى بقايمة مسطّحة (files.list بـ mimeType من غير parents) + Changes API بدل الزحف على المجلدات.
/// </summary>
public interface IGoogleDriveClient
{
    bool IsConfigured { get; }
    DriveStateSnapshot GetState();

    /// <summary>عدّادات تراكمية (للقياس: بنطرح قيمة البداية من قيمة النهاية).</summary>
    long RequestCount { get; }
    long ThrottledCount { get; }
    long ErrorCount { get; }

    /// <summary>فحص حي: بيرجّع إيميل الحساب المربوط، أو بيرمي DriveAuthException.</summary>
    Task<string?> CheckConnectionAsync(CancellationToken ct = default);

    /// <summary>فحص تشخيصي لأسلوب الاكتشاف على الحساب ده (قايمة مسطّحة + Changes token + sharedWithMe).</summary>
    Task<DriveProbeResult> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// صفحة واحدة من كل PDF متاح للحساب (على أي عمق، بما فيها الاختصارات لـ PDF). من غير زحف على المجلدات.
    /// createdAfter: يفلتر بـ createdTime (للـ Scan الدوري).
    /// </summary>
    Task<DrivePage> ListPdfPageAsync(DateTime? createdAfter, string? pageToken, CancellationToken ct = default);

    Task<string> GetStartPageTokenAsync(CancellationToken ct = default);
    Task<DriveChangesPage> ListChangesPageAsync(string pageToken, CancellationToken ct = default);

    /// <summary>بيحمّل خريطة id→اسم لكل المجلدات المتاحة (طلبات قليلة: ~1 لكل 1000 مجلد).</summary>
    Task<int> RefreshFolderNamesAsync(CancellationToken ct = default);
    Task<string?> GetFolderNameAsync(string folderId, CancellationToken ct = default);

    /// <summary>تحميل لـ stream موجود (للاستخراج).</summary>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken ct = default);

    /// <summary>
    /// فتح stream للقراءة مباشرة من Drive (عرض/تنزيل للمستخدم) من غير تخزين على السيرفر.
    /// بيرمي DriveFileNotFoundException / DriveAuthException / DriveThrottledException.
    /// </summary>
    Task<Stream> OpenReadAsync(string fileId, CancellationToken ct = default);

    void ReportSync(int newFiles, string? error);

    // ----- قديم: زحف BFS. متساب للرجوع/المقارنة بس، مش بيتستخدم في المزامنة. -----
    [Obsolete("Replaced by ListPdfPageAsync/ListChangesPageAsync: the BFS crawl was the sync bottleneck.")]
    Task<List<DriveFileInfo>> ListAllSharedPdfsAsync(CancellationToken ct = default);
}
