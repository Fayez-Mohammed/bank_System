namespace Bank.Services.Interfaces.ICertificate;

/// <summary>عنصر واحد من Drive (ملف / مجلد / اختصار) بالحقول اللي المزامنة محتاجاها بس.</summary>
public record DriveItem(
    string Id,
    string Name,
    string MimeType,
    string? ParentId,
    DateTime? CreatedAt,
    DateTime? ModifiedAt,
    long? Size,
    bool Trashed,
    bool SharedWithMe,
    string? ShortcutTargetId,
    string? ShortcutTargetMimeType)
{
    public const string FolderMime = "application/vnd.google-apps.folder";
    public const string PdfMime = "application/pdf";
    public const string ShortcutMime = "application/vnd.google-apps.shortcut";

    public bool IsFolder => MimeType == FolderMime;
    public bool IsPdf => MimeType == PdfMime;
    public bool IsShortcut => MimeType == ShortcutMime && !string.IsNullOrEmpty(ShortcutTargetId);
}

public record DriveListPage(IReadOnlyList<DriveItem> Items, string? NextPageToken);

/// <summary>تغيير واحد من Changes API. Item = null لو الملف اتشال أو الحساب ماعادش شايفه.</summary>
public record DriveChange(string FileId, bool Removed, DriveItem? Item);

public record DriveChangesPage(IReadOnlyList<DriveChange> Changes, string? NextPageToken, string? NewStartPageToken);

/// <summary>Stream مباشر من Drive (مش متخزن على السيرفر). لازم يتعمله Dispose.</summary>
public sealed class DriveFileStream : IAsyncDisposable, IDisposable
{
    public DriveFileStream(Stream content, long? length, IDisposable? owner)
    {
        Content = content;
        Length = length;
        _owner = owner;
    }

    private readonly IDisposable? _owner;
    public Stream Content { get; }
    public long? Length { get; }

    public void Dispose()
    {
        Content.Dispose();
        _owner?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>NotConfigured | Unknown | Ok | NeedsReauth. بيتحفظ في الذاكرة بس (مش في الـ DB).</summary>
public record DriveStateSnapshot(
    string State, string? AccountEmail, DateTime? LastSyncAt, int LastSyncNewFiles, string? LastError);

/// <summary>عدّادات طلبات Drive من أول ما التطبيق اشتغل (للمتابعة: throttling / أخطاء / إعادة محاولة).</summary>
public record DriveApiStats(
    long Requests, long Retries, long Throttled, long Failures, DateTime? LastThrottledAt, string? LastFailure);

/// <summary>Drive مش شغال: إعدادات ناقصة، أو الـ Refresh Token اتلغى/انتهى.</summary>
public class DriveAuthException : Exception
{
    public DriveAuthException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>الملف مش موجود في Drive (اتمسح / الصلاحية اتشالت).</summary>
public class DriveFileNotFoundException : Exception
{
    public DriveFileNotFoundException(string fileId) : base($"الملف مش موجود في Google Drive (أو الصلاحية اتشالت): {fileId}") { }
}

/// <summary>الـ page token اللي متخزن مابقاش صالح (Drive رجّع 400/404 عليه).</summary>
public class DrivePageTokenInvalidException : Exception
{
    public DrivePageTokenInvalidException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Drive واحد global للنظام كله. الإعدادات (ClientId / ClientSecret / RefreshToken) من الـ configuration مرة واحدة.
/// كل الدوال بتعمل طلب واحد (صفحة واحدة) بس، والمزامنة هي اللي بتقرر تمشي لحد فين وتحفظ مكانها.
/// كل الطلبات عليها إعادة محاولة تلقائية (exponential backoff) على 429 / 5xx / rate limit.
/// </summary>
public interface IGoogleDriveClient
{
    bool IsConfigured { get; }
    DriveStateSnapshot GetState();
    DriveApiStats GetApiStats();

    /// <summary>فحص حي: بيرجّع إيميل الحساب المربوط، أو بيرمي DriveAuthException.</summary>
    Task<string?> CheckConnectionAsync(CancellationToken ct = default);

    /// <summary>صفحة واحدة من files.list على كل اللي الحساب شايفه (corpora=user)، بـ query مسطّحة (من غير زحف مجلدات).</summary>
    Task<DriveListPage> ListPageAsync(string query, string? pageToken, int pageSize = 1000, CancellationToken ct = default,
        string? orderBy = null);

    /// <summary>Changes API: الـ token اللي بيمثّل "دلوقتي".</summary>
    Task<string> GetStartPageTokenAsync(CancellationToken ct = default);

    /// <summary>صفحة واحدة من changes.list. بيرمي DrivePageTokenInvalidException لو الـ token مابقاش صالح.</summary>
    Task<DriveChangesPage> ListChangesPageAsync(string pageToken, int pageSize = 1000, CancellationToken ct = default);

    /// <summary>metadata عنصر واحد، أو null لو مش موجود / مش متاح.</summary>
    Task<DriveItem?> GetItemAsync(string id, CancellationToken ct = default);

    /// <summary>Stream مباشر لمحتوى الملف (بيرمي DriveFileNotFoundException لو مش موجود).</summary>
    Task<DriveFileStream> OpenReadAsync(string fileId, CancellationToken ct = default);

    /// <summary>تحميل الملف كله في destination (بيستخدم OpenReadAsync). maxBytes &gt; 0 = حد أقصى للحجم.</summary>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken ct = default, long maxBytes = 0);

    void ReportSync(int newFiles, string? error);
}
