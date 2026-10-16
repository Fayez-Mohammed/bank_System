using Bank.Services.Interfaces.ICertificate;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// Drive واحد global (Singleton). الإعدادات مرة واحدة:
///   GoogleDrive:ClientId / GoogleDrive:ClientSecret / GoogleDrive:RefreshToken
/// (user-secrets أو environment variables، مش appsettings.json).
/// مفيش أي حاجة بتتخزن في الـ DB: حالة الربط في الذاكرة بس.
/// </summary>
public sealed class GoogleDriveClient : IGoogleDriveClient
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string PdfMime = "application/pdf";
    private const string ShortcutMime = "application/vnd.google-apps.shortcut";

    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleDriveClient> _logger;
    private readonly object _gate = new();

    private UserCredential? _credential;
    private string _state = "Unknown";
    private string? _accountEmail;
    private DateTime? _lastSyncAt;
    private int _lastSyncNewFiles;
    private string? _lastError;

    public GoogleDriveClient(IConfiguration configuration, ILogger<GoogleDriveClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["GoogleDrive:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_configuration["GoogleDrive:ClientSecret"]) &&
        !string.IsNullOrWhiteSpace(_configuration["GoogleDrive:RefreshToken"]);

    public DriveStateSnapshot GetState()
    {
        lock (_gate)
        {
            return new DriveStateSnapshot(
                IsConfigured ? _state : "NotConfigured",
                _accountEmail, _lastSyncAt, _lastSyncNewFiles, _lastError);
        }
    }

    public void ReportSync(int newFiles, string? error)
    {
        lock (_gate)
        {
            _lastSyncAt = DateTime.UtcNow;
            _lastSyncNewFiles = newFiles;
            _lastError = error;
        }
    }

    // ============================ Credentials ============================

    private UserCredential GetCredential()
    {
        lock (_gate)
        {
            if (_credential != null) return _credential;

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _configuration["GoogleDrive:ClientId"],
                    ClientSecret = _configuration["GoogleDrive:ClientSecret"]
                },
                Scopes = new[] { DriveService.Scope.DriveReadonly }
            });

            // الـ credential بيتعمل مرة واحدة ويجدّد الـ Access Token لوحده
            _credential = new UserCredential(flow, "drive-sync",
                new TokenResponse { RefreshToken = _configuration["GoogleDrive:RefreshToken"] });
            return _credential;
        }
    }

    private static bool IsAuthRevoked(TokenResponseException ex) =>
        ex.Error?.Error is "invalid_grant" or "invalid_client" or "unauthorized_client";

    private void SetState(string state, string? error = null)
    {
        lock (_gate)
        {
            _state = state;
            if (error != null) _lastError = error;
        }
    }

    private async Task<T> WithDriveAsync<T>(Func<DriveService, Task<T>> action)
    {
        if (!IsConfigured)
            throw new DriveAuthException("إعدادات Google Drive ناقصة (GoogleDrive:ClientId / ClientSecret / RefreshToken).");

        using var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GetCredential(),
            ApplicationName = _configuration["GoogleDrive:ApplicationName"] ?? "Bank.Certificates"
        });

        try
        {
            var result = await action(service);
            SetState("Ok");
            return result;
        }
        catch (TokenResponseException ex) when (IsAuthRevoked(ex))
        {
            var reason = ex.Error?.Error ?? ex.Message;
            _logger.LogWarning("Drive refresh token rejected: {Reason}", reason);
            SetState("NeedsReauth", reason);
            throw new DriveAuthException("جوجل رفضت الـ Refresh Token (اتلغى أو انتهى). لازم تولّد توكن جديد وتحدّث الإعدادات.", ex);
        }
    }

    // ============================ API ============================

    public Task<string?> CheckConnectionAsync(CancellationToken ct = default) =>
        WithDriveAsync<string?>(async service =>
        {
            var req = service.About.Get();
            req.Fields = "user(emailAddress)";
            var about = await req.ExecuteAsync(ct);
            lock (_gate) _accountEmail = about.User?.EmailAddress;
            return about.User?.EmailAddress;
        });

    /// <summary>
    /// كل ملف PDF في "Shared with me": الملفات المشتركة مباشرة + كل اللي جوه المجلدات المشتركة (على أي عمق).
    /// </summary>
    public Task<List<DriveFileInfo>> ListAllSharedPdfsAsync(CancellationToken ct = default) =>
        WithDriveAsync(async service =>
        {
            var files = new List<DriveFileInfo>();
            var seenFiles = new HashSet<string>();
            var visitedFolders = new HashSet<string>();
            var folderQueue = new Queue<string>();

            void Collect(IList<Google.Apis.Drive.v3.Data.File>? items)
            {
                if (items == null) return;
                foreach (var f in items)
                {
                    if (f.MimeType == FolderMime)
                    {
                        if (!visitedFolders.Contains(f.Id)) folderQueue.Enqueue(f.Id);
                    }
                    else if (f.MimeType == PdfMime)
                    {
                        if (seenFiles.Add(f.Id))
                            files.Add(new DriveFileInfo(f.Id, f.Name, f.CreatedTimeDateTimeOffset?.UtcDateTime));
                    }
                    else if (f.MimeType == ShortcutMime && !string.IsNullOrEmpty(f.ShortcutDetails?.TargetId))
                    {
                        var targetId = f.ShortcutDetails!.TargetId;
                        if (f.ShortcutDetails.TargetMimeType == FolderMime)
                        {
                            if (!visitedFolders.Contains(targetId)) folderQueue.Enqueue(targetId);
                        }
                        else if (f.ShortcutDetails.TargetMimeType == PdfMime && seenFiles.Add(targetId))
                        {
                            files.Add(new DriveFileInfo(targetId, f.Name, f.CreatedTimeDateTimeOffset?.UtcDateTime));
                        }
                    }
                }
            }

            const string fields = "nextPageToken, files(id, name, mimeType, createdTime, shortcutDetails(targetId, targetMimeType))";

            // 1) الأصناف المشتركة مباشرة مع الحساب (مجلدات وملفات)
            string? pageToken = null;
            do
            {
                ct.ThrowIfCancellationRequested();
                var req = service.Files.List();
                req.Q = "sharedWithMe = true and trashed = false";
                req.Fields = fields;
                req.PageSize = 1000;
                req.PageToken = pageToken;
                req.SupportsAllDrives = true;
                req.IncludeItemsFromAllDrives = true;

                var resp = await req.ExecuteAsync(ct);
                Collect(resp.Files);
                pageToken = resp.NextPageToken;
            } while (pageToken != null);

            // 2) الزحف على كل المجلدات (BFS)
            while (folderQueue.Count > 0)
            {
                var folderId = folderQueue.Dequeue();
                if (!visitedFolders.Add(folderId)) continue;

                pageToken = null;
                do
                {
                    ct.ThrowIfCancellationRequested();
                    var req = service.Files.List();
                    req.Q = $"'{EscapeId(folderId)}' in parents and trashed = false";
                    req.Fields = fields;
                    req.PageSize = 1000;
                    req.PageToken = pageToken;
                    req.SupportsAllDrives = true;
                    req.IncludeItemsFromAllDrives = true;

                    var resp = await req.ExecuteAsync(ct);
                    Collect(resp.Files);
                    pageToken = resp.NextPageToken;
                } while (pageToken != null);
            }

            return files;
        });

    public Task DownloadAsync(string fileId, Stream destination, CancellationToken ct = default) =>
        WithDriveAsync(async service =>
        {
            var get = service.Files.Get(fileId);
            get.SupportsAllDrives = true;
            var progress = await get.DownloadAsync(destination, ct);
            if (progress.Status != DownloadStatus.Completed)
                throw new IOException("فشل تحميل الملف من Drive: " + (progress.Exception?.Message ?? progress.Status.ToString()), progress.Exception);
            return true;
        });

    /// <summary>IDs جوجل حروف وأرقام و - و _ بس. أي حاجة تانية بتتشال عشان مانكسرش الـ query.</summary>
    private static string EscapeId(string id) =>
        new(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
}
