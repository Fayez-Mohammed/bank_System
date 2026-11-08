using System.Net;
using System.Net.Http.Headers;
using Bank.Services.Helpers;
using Bank.Services.Interfaces.ICertificate;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// Drive واحد global (Singleton). الإعدادات مرة واحدة:
///   GoogleDrive:ClientId / GoogleDrive:ClientSecret / GoogleDrive:RefreshToken
/// (user-secrets أو environment variables، مش appsettings.json).
///
/// - DriveService واحد بيتعاد استخدامه (HttpClient واحد) بدل واحد جديد لكل طلب.
/// - كل طلب عليه إعادة محاولة (exponential backoff + jitter) على 429 / 5xx / rate limit / أخطاء الشبكة.
/// - مفيش أي دالة بتزحف على مجلدات: كل دالة = طلب واحد، والمزامنة هي اللي بتحفظ مكانها.
/// </summary>
public sealed class GoogleDriveClient : IGoogleDriveClient, IDisposable
{
    private const string ItemFields =
        "id, name, mimeType, parents, createdTime, modifiedTime, size, trashed, sharedWithMeTime, shortcutDetails(targetId, targetMimeType)";

    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleDriveClient> _logger;
    private readonly object _gate = new();
    private readonly int _maxRetries;

    private UserCredential? _credential;
    private DriveService? _service;
    private string _state = "Unknown";
    private string? _accountEmail;
    private DateTime? _lastSyncAt;
    private int _lastSyncNewFiles;
    private string? _lastError;

    private long _requests, _retries, _throttled, _failures;
    private DateTime? _lastThrottledAt;
    private string? _lastFailure;

    public GoogleDriveClient(IConfiguration configuration, ILogger<GoogleDriveClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _maxRetries = int.TryParse(configuration["GoogleDrive:MaxRetries"], out var r) && r >= 0 ? r : 6;
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

    public DriveApiStats GetApiStats()
    {
        lock (_gate)
        {
            return new DriveApiStats(
                Interlocked.Read(ref _requests), Interlocked.Read(ref _retries),
                Interlocked.Read(ref _throttled), Interlocked.Read(ref _failures),
                _lastThrottledAt, _lastFailure);
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

    public void Dispose()
    {
        lock (_gate)
        {
            _service?.Dispose();
            _service = null;
        }
    }

    // ============================ Service / credentials ============================

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
                ApplicationName = string.IsNullOrWhiteSpace(_configuration["GoogleDrive:ApplicationName"])
                    ? "Bank.Certificates"
                    : _configuration["GoogleDrive:ApplicationName"],
                // إعادة المحاولة بتاعتنا (تحت) بتغطي 429 و 403 rate limit كمان، فبنقفل بتاعة المكتبة عشان مانكرّرش
                DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
            });
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

    private static bool IsThrottle(Exception ex) => ex switch
    {
        GoogleApiException g => g.HttpStatusCode == (HttpStatusCode)429 ||
                                (g.HttpStatusCode == HttpStatusCode.Forbidden &&
                                 g.Error?.Errors?.Any(e => e.Reason is "rateLimitExceeded" or "userRateLimitExceeded") == true),
        HttpRequestException h => h.StatusCode == (HttpStatusCode)429,
        _ => false
    };

    private static bool IsTransient(Exception ex, CancellationToken ct) => ex switch
    {
        _ when IsThrottle(ex) => true,
        GoogleApiException g => (int)g.HttpStatusCode >= 500 ||
                                (g.HttpStatusCode == HttpStatusCode.Forbidden &&
                                 g.Error?.Errors?.Any(e => e.Reason is "backendError" or "internalError") == true),
        HttpRequestException h => h.StatusCode == null || (int)h.StatusCode >= 500,
        OperationCanceledException => !ct.IsCancellationRequested,   // timeout مش إلغاء
        IOException => true,
        _ => false
    };

    /// <summary>كل طلب Drive بيعدّي من هنا: إعدادات + auth + إعادة محاولة + عدّادات.</summary>
    private async Task<T> ExecuteAsync<T>(string operation, Func<DriveService, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new DriveAuthException("إعدادات Google Drive ناقصة (GoogleDrive:ClientId / ClientSecret / RefreshToken).");

        var service = GetService();
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requests);
            CertificateMetrics.DriveRequests.Add(1, new KeyValuePair<string, object?>("op", operation));
            try
            {
                var result = await action(service, ct);
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
            catch (Exception ex) when (attempt < _maxRetries && IsTransient(ex, ct))
            {
                var throttled = IsThrottle(ex);
                Interlocked.Increment(ref _retries);
                CertificateMetrics.DriveRetries.Add(1, new KeyValuePair<string, object?>("op", operation));
                if (throttled)
                {
                    Interlocked.Increment(ref _throttled);
                    CertificateMetrics.DriveThrottled.Add(1, new KeyValuePair<string, object?>("op", operation));
                    lock (_gate) _lastThrottledAt = DateTime.UtcNow;
                }

                var delay = TimeSpan.FromSeconds(Math.Min(64, Math.Pow(2, attempt))) +
                            TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
                _logger.LogWarning(
                    "Drive {Operation} failed ({Kind}: {Error}); retry {Attempt}/{Max} in {DelayMs}ms",
                    operation, throttled ? "throttled" : "transient", ex.Message, attempt + 1, _maxRetries,
                    (int)delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _failures);
                CertificateMetrics.DriveFailures.Add(1, new KeyValuePair<string, object?>("op", operation));
                lock (_gate) _lastFailure = $"{DateTime.UtcNow:O} {operation}: {ex.Message}";
                throw;
            }
        }
    }

    private static DriveItem Map(DriveFile f) => new(
        f.Id,
        f.Name ?? "",
        f.MimeType ?? "",
        f.Parents?.FirstOrDefault(),
        f.CreatedTimeDateTimeOffset?.UtcDateTime,
        f.ModifiedTimeDateTimeOffset?.UtcDateTime,
        f.Size,
        f.Trashed == true,
        f.SharedWithMeTimeDateTimeOffset != null,
        f.ShortcutDetails?.TargetId,
        f.ShortcutDetails?.TargetMimeType);

    // ============================ API ============================

    public Task<string?> CheckConnectionAsync(CancellationToken ct = default) =>
        ExecuteAsync<string?>("about.get", async (service, token) =>
        {
            var req = service.About.Get();
            req.Fields = "user(emailAddress)";
            var about = await req.ExecuteAsync(token);
            lock (_gate) _accountEmail = about.User?.EmailAddress;
            return about.User?.EmailAddress;
        }, ct);

    public Task<DriveListPage> ListPageAsync(string query, string? pageToken, int pageSize = 1000, CancellationToken ct = default,
        string? orderBy = null) =>
        ExecuteAsync("files.list", async (service, token) =>
        {
            var req = service.Files.List();
            req.Q = query;
            req.Corpora = "user";
            req.Spaces = "drive";
            req.Fields = $"nextPageToken, files({ItemFields})";
            req.PageSize = Math.Clamp(pageSize, 1, 1000);
            req.PageToken = pageToken;
            if (orderBy != null) req.OrderBy = orderBy;
            req.SupportsAllDrives = true;
            req.IncludeItemsFromAllDrives = true;

            try
            {
                var resp = await req.ExecuteAsync(token);
                return new DriveListPage(resp.Files?.Select(Map).ToList() ?? new List<DriveItem>(), resp.NextPageToken);
            }
            catch (GoogleApiException ex) when (pageToken != null && ex.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                throw new DrivePageTokenInvalidException("files.list page token مابقاش صالح: " + ex.Message, ex);
            }
        }, ct);

    public Task<string> GetStartPageTokenAsync(CancellationToken ct = default) =>
        ExecuteAsync("changes.getStartPageToken", async (service, token) =>
        {
            var req = service.Changes.GetStartPageToken();
            req.SupportsAllDrives = true;
            var resp = await req.ExecuteAsync(token);
            return resp.StartPageTokenValue
                   ?? throw new InvalidOperationException("Drive رجّع start page token فاضي.");
        }, ct);

    public Task<DriveChangesPage> ListChangesPageAsync(string pageToken, int pageSize = 1000, CancellationToken ct = default) =>
        ExecuteAsync("changes.list", async (service, token) =>
        {
            var req = service.Changes.List(pageToken);
            req.PageSize = Math.Clamp(pageSize, 1, 1000);
            req.Spaces = "drive";
            req.IncludeRemoved = true;
            // false = يشمل الملفات المشتركة معانا اللي مش متضافة لـ My Drive (ده الشكل بتاعنا)
            req.RestrictToMyDrive = false;
            req.SupportsAllDrives = true;
            req.IncludeItemsFromAllDrives = true;
            req.Fields = $"nextPageToken, newStartPageToken, changes(changeType, removed, fileId, file({ItemFields}))";

            try
            {
                var resp = await req.ExecuteAsync(token);
                var changes = (resp.Changes ?? new List<Google.Apis.Drive.v3.Data.Change>())
                    .Where(c => c.ChangeType is null or "file" && !string.IsNullOrEmpty(c.FileId))
                    .Select(c => new DriveChange(c.FileId, c.Removed == true, c.File == null ? null : Map(c.File)))
                    .ToList();
                return new DriveChangesPage(changes, resp.NextPageToken, resp.NewStartPageToken);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                throw new DrivePageTokenInvalidException("changes page token مابقاش صالح: " + ex.Message, ex);
            }
        }, ct);

    public Task<DriveItem?> GetItemAsync(string id, CancellationToken ct = default) =>
        ExecuteAsync("files.get", async (service, token) =>
        {
            var req = service.Files.Get(id);
            req.SupportsAllDrives = true;
            req.Fields = ItemFields;
            try
            {
                return (DriveItem?)Map(await req.ExecuteAsync(token));
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, ct);

    public Task<DriveFileStream> OpenReadAsync(string fileId, CancellationToken ct = default) =>
        ExecuteAsync("files.get(media)", async (service, token) =>
        {
            var url = $"{service.BaseUri}files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));

            // ResponseHeadersRead: المحتوى بيتقري من الشبكة وقت ما بيتبعت للمستخدم، مش بيتحمّل كله في الذاكرة
            var response = await service.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                throw new DriveFileNotFoundException(fileId);
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"Drive رجّع {(int)status} وهو بيحمّل الملف {fileId}", null, status);
            }

            var stream = await response.Content.ReadAsStreamAsync(token);
            return new DriveFileStream(stream, response.Content.Headers.ContentLength, response);
        }, ct);

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken ct = default, long maxBytes = 0)
    {
        await using var file = await OpenReadAsync(fileId, ct);
        if (maxBytes > 0 && file.Length > maxBytes)
            throw new InvalidDataException($"حجم الملف ({file.Length} بايت) أكبر من الحد المسموح ({maxBytes}).");

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await file.Content.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (maxBytes > 0 && total > maxBytes)
                throw new InvalidDataException($"حجم الملف أكبر من الحد المسموح ({maxBytes} بايت).");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
