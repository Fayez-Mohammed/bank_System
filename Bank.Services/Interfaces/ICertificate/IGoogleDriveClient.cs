namespace Bank.Services.Interfaces.ICertificate;

public record DriveFileInfo(string FileId, string Name, DateTime? CreatedAt);

/// <summary>NotConfigured | Unknown | Ok | NeedsReauth. بيتحفظ في الذاكرة بس (مش في الـ DB).</summary>
public record DriveStateSnapshot(
    string State, string? AccountEmail, DateTime? LastSyncAt, int LastSyncNewFiles, string? LastError);

/// <summary>Drive مش شغال: إعدادات ناقصة، أو الـ Refresh Token اتلغى/انتهى.</summary>
public class DriveAuthException : Exception
{
    public DriveAuthException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Drive واحد global للنظام كله. الإعدادات (ClientId / ClientSecret / RefreshToken) من الـ configuration مرة واحدة.
/// أي ملف PDF في "Shared with me" (بما فيها كل المجلدات الفرعية) بيتحسب شهادة.
/// </summary>
public interface IGoogleDriveClient
{
    bool IsConfigured { get; }
    DriveStateSnapshot GetState();

    /// <summary>فحص حي: بيرجّع إيميل الحساب المربوط، أو بيرمي DriveAuthException.</summary>
    Task<string?> CheckConnectionAsync(CancellationToken ct = default);

    Task<List<DriveFileInfo>> ListAllSharedPdfsAsync(CancellationToken ct = default);

    Task DownloadAsync(string fileId, Stream destination, CancellationToken ct = default);

    void ReportSync(int newFiles, string? error);
}
