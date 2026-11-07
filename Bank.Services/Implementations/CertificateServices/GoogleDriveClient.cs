using System.Collections.Concurrent;
using System.Net;
using Bank.Services.Helpers;
using Bank.Services.Interfaces.ICertificate;
using Google;
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
///
/// قرارات التصميم:
///  • DriveService واحد مشترك (thread-safe) بدل واحد لكل طلب: بيمنع استنزاف sockets مع آلاف التحميلات.
///  • كل طلب بيعدّي على WithDriveAsync: عدّاد + retry بـ exponential backoff + jitter على 429/5xx/rate-limit/أخطاء الشبكة.
///  • الاكتشاف بـ files.list مسطّح (mimeType من غير parents) بدل BFS على المجلدات.
/// </summary>
public sealed class GoogleDriveClient : IGoogleDriveClient, IDisposable
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string PdfMime = "application/pdf";
    private const string ShortcutMime = "application/vnd.google-apps.shortcut";

    private const string FileFields =
        "id, name, mimeType, createdTime, modifiedTime, size, trashed, parents, shortcutDetails(targetId, targetMimeType)";

    private readonly IConfiguration _configuration;
    private readonly CertificateOptions _options;
    private readonly ILogger<GoogleDriveClient> _logger;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, string?> _folderNames = new();

    private UserCredential? _credential;
    private DriveService? _service;
    private string _state = "Unknown";
    private string? _accountEmail;
    private DateTime? _lastSyncAt;
    private int _lastSyncNewFiles;
    private string? _lastError;

    private long _requests;
    private long _throttled;
    private long _errors;

    public GoogleDriveClient(IConfiguration configuration, CertificateOptions options, ILogger<GoogleDriveClient> logger)
    {
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["GoogleDrive:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_configuration["GoogleDrive:ClientSecret"]) &&
        !string.IsNullOrWhiteSpace(_configuration["GoogleDrive:RefreshToken"]);

    public long RequestCount => Interlocked.Read(ref _requests);
    public long ThrottledCount => Interlocked.Read(ref _throttled);
    public long ErrorCount => Interlocked.Read(ref _errors);

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

    public void Dispose() => _service?.Dispose();

    // ============================ Credentials / Service ============================

    private DriveService GetService()
    {
        lock (_gate)
        {
            if (_service != null) return _service;

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

            _service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = _configuration["GoogleDrive:ApplicationName"] ?? "Bank.Certificates"
            });
            _service.HttpClient.Timeout = TimeSpan.FromMinutes(5);
            return _service;
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

    private static bool IsRateLimit(GoogleApiException ex) =>
        ex.HttpStatusCode == HttpStatusCode.TooManyRequests ||
        (ex.HttpStatusCode == HttpStatusCode.Forbidden &&
         ex.Error?.Errors?.Any(e => e.Reason is "rateLimitExceeded" or "userRateLimitExceeded" or "dailyLimitExceeded" or "sharingRateLimitExceeded") == true);

    private static bool IsServerError(GoogleApiException ex) =>
        (int)ex.HttpStatusCode is 500 or 502 or 503 or 504;

    /// <summary>
    /// كل طلب لـ Drive بيعدّي من هنا: عدّاد + retry/backoff. بيحوّل الأخطاء لاستثناءات واضحة للـ callers:
    /// DriveAuthException (توكن)، DriveThrottledException (quota)، DriveFileNotFoundException (404).
    /// </summary>
    private async Task<T> WithDriveAsync<T>(Func<DriveService, Task<T>> action, CancellationToken ct, string? fileIdForNotFound = null)
    {
        if (!IsConfigured)
            throw new DriveAuthException("إعدادات Google Drive ناقصة (GoogleDrive:ClientId / ClientSecret / RefreshToken).");

        var max = Math.Max(1, _options.DriveMaxRetries);
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requests);
            CertificateMetrics.DriveRequests.Add(1);

            try
            {
                var result = await action(GetService());
                SetState("Ok");
                return result;
            }
            catch (TokenResponseException ex) when (IsAuthRevoked(ex))
            {
                var reason = ex.Error?.Error ?? ex.Message;
                _logger.LogWarning("Drive refresh token rejected: {Reason}", reason);
                SetState("NeedsReauth", reason);
                Interlocked.Increment(ref _errors);
                CertificateMetrics.DriveErrors.Add(1);
                throw new DriveAuthException("جوجل رفضت الـ Refresh Token (اتلغى أو انتهى). لازم تولّد توكن جديد وتحدّث الإعدادات.", ex);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound && fileIdForNotFound != null)
            {
                throw new DriveFileNotFoundException(fileIdForNotFound, ex);
            }
            catch (GoogleApiException ex) when (IsRateLimit(ex) || IsServerError(ex))
            {
                var rate = IsRateLimit(ex);
                if (rate) { Interlocked.Increment(ref _throttled); CertificateMetrics.DriveThrottled.Add(1); }
                else { Interlocked.Increment(ref _errors); CertificateMetrics.DriveErrors.Add(1); }

                if (attempt >= max)
                    throw rate ? new DriveThrottledException("Drive quota/rate limit: " + ex.Message, ex) : ex;

                var delay = Backoff(attempt);
                _logger.LogWarning("Drive {Kind} (HTTP {Status}); retry {Attempt}/{Max} in {Delay}ms",
                    rate ? "rate-limited" : "server error", (int)ex.HttpStatusCode, attempt, max, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _errors);
                CertificateMetrics.DriveErrors.Add(1);
                if (attempt >= max) throw;

                var delay = Backoff(attempt);
                _logger.LogWarning(ex, "Drive network error; retry {Attempt}/{Max} in {Delay}ms", attempt, max, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static TimeSpan Backoff(int attempt)
    {
        var baseMs = Math.Min(30_000, 500 * Math.Pow(2, attempt - 1));
        var jitter = Random.Shared.NextDouble() * 0.4 + 0.8;   // 0.8x .. 1.2x
        return TimeSpan.FromMilliseconds(baseMs * jitter);
    }

    // ============================ Mapping ============================

    /// <summary>PDF عادي → DriveFileInfo. اختصار لـ PDF → بيرجّع الهدف (باسم الاختصار). غير كده → null.</summary>
    private static DriveFileInfo? MapPdf(Google.Apis.Drive.v3.Data.File f)
    {
        var created = f.CreatedTimeDateTimeOffset?.UtcDateTime;
        var modified = f.ModifiedTimeDateTimeOffset?.UtcDateTime;
        var parent = f.Parents?.FirstOrDefault();

        if (f.MimeType == PdfMime)
            return new DriveFileInfo(f.Id, f.Name, created, modified, parent, f.Size, f.Trashed == true);

        if (f.MimeType == ShortcutMime &&
            f.ShortcutDetails?.TargetMimeType == PdfMime &&
            !string.IsNullOrEmpty(f.ShortcutDetails.TargetId))
        {
            return new DriveFileInfo(f.ShortcutDetails.TargetId, f.Name, created, modified, parent, null, f.Trashed == true);
        }

        return null;
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
        }, ct);

    private FilesResource.ListRequest NewListRequest(DriveService service, string q, string fields, int pageSize)
    {
        var req = service.Files.List();
        req.Q = q;
        req.Fields = fields;
        req.PageSize = pageSize;
        req.SupportsAllDrives = true;
        var corpora = string.IsNullOrWhiteSpace(_options.DriveCorpora) ? "user" : _options.DriveCorpora.Trim();
        req.Corpora = corpora;
        if (corpora.Equals("allDrives", StringComparison.OrdinalIgnoreCase)) req.IncludeItemsFromAllDrives = true;
        return req;
    }

    public Task<DrivePage> ListPdfPageAsync(DateTime? createdAfter, string? pageToken, CancellationToken ct = default) =>
        WithDriveAsync(async service =>
        {
            // قايمة مسطّحة: مفيش "'folderId' in parents" => Drive بيرجّع كل PDF الحساب شايفه على أي عمق (مشارك أو متنقل).
            var q = $"trashed = false and (mimeType = '{PdfMime}' or mimeType = '{ShortcutMime}')";
            if (createdAfter.HasValue)
                q += $" and createdTime > '{createdAfter.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}'";

            var req = NewListRequest(service, q,
                $"nextPageToken, files({FileFields})", Math.Clamp(_options.DrivePageSize, 100, 1000));
            req.PageToken = pageToken;
            if (createdAfter.HasValue) req.OrderBy = "createdTime";

            var resp = await req.ExecuteAsync(ct);
            var files = (resp.Files ?? new List<Google.Apis.Drive.v3.Data.File>())
                .Select(MapPdf).Where(f => f != null).Select(f => f!).ToList();
            return new DrivePage(files, resp.NextPageToken, resp.Files?.Count ?? 0);
        }, ct);

    public Task<string> GetStartPageTokenAsync(CancellationToken ct = default) =>
        WithDriveAsync(async service =>
        {
            var req = service.Changes.GetStartPageToken();
            req.SupportsAllDrives = true;
            var resp = await req.ExecuteAsync(ct);
            return resp.StartPageTokenValue;
        }, ct);

    public Task<DriveChangesPage> ListChangesPageAsync(string pageToken, CancellationToken ct = default) =>
        WithDriveAsync(async service =>
        {
            var req = service.Changes.List(pageToken);
            req.Fields = $"nextPageToken, newStartPageToken, changes(fileId, removed, file({FileFields}))";
            req.PageSize = 1000;
            req.IncludeRemoved = true;
            req.SupportsAllDrives = true;
            req.IncludeItemsFromAllDrives = true;
            req.RestrictToMyDrive = false;

            var resp = await req.ExecuteAsync(ct);
            var changes = new List<DriveChange>();
            foreach (var c in resp.Changes ?? new List<Google.Apis.Drive.v3.Data.Change>())
            {
                var removed = c.Removed == true || c.File?.Trashed == true;
                var file = c.File == null ? null : MapPdf(c.File);
                changes.Add(new DriveChange(c.FileId, removed, removed ? null : file));
            }
            return new DriveChangesPage(changes, resp.NextPageToken, resp.NewStartPageToken);
        }, ct);

    public async Task<int> RefreshFolderNamesAsync(CancellationToken ct = default)
    {
        var count = 0;
        string? pageToken = null;
        do
        {
            var token = pageToken;
            var (items, next) = await WithDriveAsync(async service =>
            {
                var req = NewListRequest(service, $"trashed = false and mimeType = '{FolderMime}'",
                    "nextPageToken, files(id, name)", 1000);
                req.PageToken = token;
                var resp = await req.ExecuteAsync(ct);
                return (resp.Files ?? new List<Google.Apis.Drive.v3.Data.File>(), resp.NextPageToken);
            }, ct);

            foreach (var f in items) { _folderNames[f.Id] = f.Name; count++; }
            pageToken = next;
        } while (pageToken != null);

        return count;
    }

    public async Task<string?> GetFolderNameAsync(string folderId, CancellationToken ct = default)
    {
        if (_folderNames.TryGetValue(folderId, out var cached)) return cached;

        string? name = null;
        try
        {
            name = await WithDriveAsync<string?>(async service =>
            {
                var req = service.Files.Get(folderId);
                req.Fields = "id, name";
                req.SupportsAllDrives = true;
                return (await req.ExecuteAsync(ct)).Name;
            }, ct, fileIdForNotFound: folderId);
        }
        catch (DriveFileNotFoundException) { /* parent مش متاح للحساب (مثلاً جذر المشاركة): نكتفي بالـ id */ }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden) { }

        _folderNames[folderId] = name;
        return name;
    }

    public Task DownloadAsync(string fileId, Stream destination, CancellationToken ct = default) =>
        WithDriveAsync(async service =>
        {
            var get = service.Files.Get(fileId);
            get.SupportsAllDrives = true;
            var progress = await get.DownloadAsync(destination, ct);
            if (progress.Status != DownloadStatus.Completed)
            {
                // تحويل 404/429 اللي جوه الـ progress لاستثناء يفهمه الـ retry
                if (progress.Exception is GoogleApiException gex) throw gex;
                throw new IOException("فشل تحميل الملف من Drive: " + (progress.Exception?.Message ?? progress.Status.ToString()), progress.Exception);
            }
            return true;
        }, ct, fileIdForNotFound: fileId);

    public Task<Stream> OpenReadAsync(string fileId, CancellationToken ct = default) =>
        WithDriveAsync<Stream>(async service =>
        {
            // service.HttpClient متوصّل بالـ credential. ResponseHeadersRead = بنبدأ نبعت للمستخدم من غير ما نحمّل الملف كله في الذاكرة.
            var url = $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true";
            var response = await service.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();

                if (status == HttpStatusCode.NotFound) throw new DriveFileNotFoundException(fileId);

                // نخلّي الـ retry/backoff يتعامل مع 429/403-rate/5xx بنفس مسار GoogleApiException
                throw new GoogleApiException("Drive", $"HTTP {(int)status}: {body}")
                {
                    HttpStatusCode = status,
                    Error = TryParseError(body)
                };
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            return new ResponseOwningStream(stream, response);
        }, ct, fileIdForNotFound: fileId);

    private static Google.Apis.Requests.RequestError? TryParseError(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var e)) return null;
            var err = new Google.Apis.Requests.RequestError { Errors = new List<Google.Apis.Requests.SingleError>() };
            if (e.TryGetProperty("message", out var m)) err.Message = m.GetString();
            if (e.TryGetProperty("errors", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                    err.Errors.Add(new Google.Apis.Requests.SingleError
                    {
                        Reason = item.TryGetProperty("reason", out var r) ? r.GetString() : null
                    });
            return err;
        }
        catch { return null; }
    }

    public async Task<DriveProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var email = await CheckConnectionAsync(ct);

        var changesOk = false;
        try { changesOk = !string.IsNullOrEmpty(await GetStartPageTokenAsync(ct)); }
        catch (Exception ex) when (ex is GoogleApiException) { _logger.LogWarning(ex, "Drive probe: Changes API unavailable"); }

        var flat = await WithDriveAsync(async service =>
        {
            var req = NewListRequest(service, $"trashed = false and mimeType = '{PdfMime}'", "nextPageToken, files(id, name)", 100);
            return await req.ExecuteAsync(ct);
        }, ct);

        var topLevel = await WithDriveAsync(async service =>
        {
            var req = service.Files.List();
            req.Q = "sharedWithMe = true and trashed = false";
            req.Fields = "files(id)";
            req.PageSize = 1000;
            req.SupportsAllDrives = true;
            req.IncludeItemsFromAllDrives = true;
            return (await req.ExecuteAsync(ct)).Files?.Count ?? 0;
        }, ct);

        return new DriveProbeResult(
            email, changesOk, flat.Files?.Count ?? 0, flat.NextPageToken != null, topLevel,
            (flat.Files ?? new List<Google.Apis.Drive.v3.Data.File>()).Take(5).Select(f => f.Name).ToList());
    }

    // ============================ قديم: BFS (للمقارنة/الرجوع بس) ============================

    [Obsolete("Replaced by ListPdfPageAsync/ListChangesPageAsync.")]
    public Task<List<DriveFileInfo>> ListAllSharedPdfsAsync(CancellationToken ct = default) =>
        WithDriveAsync(async service =>
        {
            var files = new List<DriveFileInfo>();
            var seen = new HashSet<string>();
            var visited = new HashSet<string>();
            var queue = new Queue<string>();

            void Collect(IList<Google.Apis.Drive.v3.Data.File>? items)
            {
                if (items == null) return;
                foreach (var f in items)
                {
                    if (f.MimeType == FolderMime) { if (!visited.Contains(f.Id)) queue.Enqueue(f.Id); continue; }
                    var m = MapPdf(f);
                    if (m != null && seen.Add(m.FileId)) files.Add(m);
                }
            }

            string? token = null;
            do
            {
                var req = service.Files.List();
                req.Q = "sharedWithMe = true and trashed = false";
                req.Fields = $"nextPageToken, files({FileFields})";
                req.PageSize = 1000; req.PageToken = token;
                req.SupportsAllDrives = true; req.IncludeItemsFromAllDrives = true;
                var resp = await req.ExecuteAsync(ct);
                Collect(resp.Files); token = resp.NextPageToken;
            } while (token != null);

            while (queue.Count > 0)
            {
                var folder = queue.Dequeue();
                if (!visited.Add(folder)) continue;
                token = null;
                do
                {
                    var req = service.Files.List();
                    req.Q = $"'{new string(folder.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray())}' in parents and trashed = false";
                    req.Fields = $"nextPageToken, files({FileFields})";
                    req.PageSize = 1000; req.PageToken = token;
                    req.SupportsAllDrives = true; req.IncludeItemsFromAllDrives = true;
                    var resp = await req.ExecuteAsync(ct);
                    Collect(resp.Files); token = resp.NextPageToken;
                } while (token != null);
            }
            return files;
        }, ct);

    /// <summary>بيقفل الـ HttpResponseMessage مع الـ stream (عشان الـ connection ترجع للـ pool).</summary>
    private sealed class ResponseOwningStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ResponseOwningStream(Stream inner, HttpResponseMessage response) { _inner = inner; _response = response; }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _inner.ReadAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _inner.Dispose(); _response.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
