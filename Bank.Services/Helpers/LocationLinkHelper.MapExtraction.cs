using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Bank.Services.Helpers;

// partial عشان الكلاس الأصلي LocationLinkHelper (فيه ToPoint/ToGoogleMapsLink)
// موجود بالفعل وما عايزينش نلمسه — ده بس بيضيف استخراج الإحداثيات من اللينكات.
public static partial class LocationLinkHelper
{
    // !3d<lat>!4d<lng> جوه data= هو مكان الدبوس نفسه، فبنقدّمه على @ (اللي هو مركز الشاشة بس).
    private static readonly Regex PinCoordinatePattern = new(
        @"!3d(-?\d{1,3}\.\d+)!4d(-?\d{1,3}\.\d+)",
        RegexOptions.Compiled);

    // أشكال اللينكات: ?q= / ?ll= / ?query= / destination= / daddr= / /place/<lat>,<lng> / /search/<lat>,<lng> / @
    // الفاصلة ممكن تيجي "," أو "%2C" وبعدها مسافة ("+" أو "%20").
    private static readonly Regex CoordinatePattern = new(
        @"(?:destination=|daddr=|[?&](?:q|ll|query)=(?:loc:)?|/place/|/search/|@)(-?\d{1,3}\.\d+)\s*(?:,|%2C)\s*(?:\+|%20)?(-?\d{1,3}\.\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // اللينكات الجديدة (maps.app.goo.gl) ساعات بتحوّل على اسم مكان من غير إحداثيات
    // (/maps/place/<اسم>/data=!...!1s0x..:0x..)، فبنجيب صفحة المكان ونطلب منها preview/place.
    private static readonly Regex PlacePreviewLinkPattern = new(
        @"href=""(/maps/preview/place\?[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex PlacePreviewCoordinatePattern = new(
        @"\[null,null,(-?\d{1,3}\.\d+),(-?\d{1,3}\.\d+)\],""0x[0-9a-f]+:0x[0-9a-f]+""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ShortLinkHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "maps.app.goo.gl", "goo.gl"
    };

    private static readonly HashSet<string> GoogleMapsHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "google.com", "www.google.com", "maps.google.com"
    };

    private const int MaxRedirectHops = 5;
    private const int MaxConcurrentResolves = 8;

    /// <summary>
    /// بيحاول يستخرج (Latitude, Longitude) من رابط خرائط جوجل — سواء كان فيه
    /// الإحداثيات مباشرة، أو رابط مختصر (maps.app.goo.gl/goo.gl) بيتابعه عبر
    /// الـ redirects لحد ما يلاقي رابط فيه إحداثيات، ولو وصل لاسم مكان من غير
    /// إحداثيات بيجيبها من صفحة المكان نفسها.
    /// </summary>
    public static async Task<(double Latitude, double Longitude)?> TryExtractCoordinatesAsync(
        string? url, HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();

        var direct = TryExtractFromUrlText(url);
        if (direct.HasValue) return direct;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!ShortLinkHosts.Contains(uri.Host) && !GoogleMapsHosts.Contains(uri.Host)) return null;

        var resolvedUrl = await ResolveRedirectsAsync(uri, httpClient, cancellationToken);
        if (resolvedUrl is null) return null;

        var fromResolved = TryExtractFromUrlText(resolvedUrl);
        if (fromResolved.HasValue) return fromResolved;

        return await TryExtractFromPlacePageAsync(new Uri(resolvedUrl), httpClient, cancellationToken);
    }

    /// <summary>
    /// نسخة Batch — بتعمل Distinct على اللينكات وتحل كل لينك فريد مرة واحدة بس
    /// (لحد MaxConcurrentResolves لينك في نفس الوقت).
    /// المفتاح في الـ Dictionary الراجعة هو الرابط الأصلي كما هو.
    /// </summary>
    public static async Task<Dictionary<string, (double Latitude, double Longitude)?>> ExtractCoordinatesBatchAsync(
        IEnumerable<string?> urls, HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        var distinctUrls = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!).Distinct().ToList();
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, (double, double)?>();

        await Parallel.ForEachAsync(
            distinctUrls,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentResolves, CancellationToken = cancellationToken },
            async (url, ct) => result[url] = await TryExtractCoordinatesAsync(url, httpClient, ct));

        return new Dictionary<string, (double Latitude, double Longitude)?>(result);
    }

    private static (double, double)? TryExtractFromUrlText(string url)
    {
        return TryParseMatch(PinCoordinatePattern.Match(url)) ?? TryParseMatch(CoordinatePattern.Match(url));
    }

    private static (double, double)? TryParseMatch(Match match)
    {
        if (!match.Success) return null;

        if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) &&
            lat is >= -90 and <= 90 && lng is >= -180 and <= 180)
        {
            return (lat, lng);
        }

        return null;
    }

    private static async Task<(double, double)?> TryExtractFromPlacePageAsync(
        Uri placeUri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (!GoogleMapsHosts.Contains(placeUri.Host)) return null;

        var page = await TryGetStringAsync(placeUri, httpClient, cancellationToken);
        if (page is null) return null;

        var previewLink = PlacePreviewLinkPattern.Match(page);
        if (!previewLink.Success) return null;

        var previewUri = new Uri(placeUri, WebUtility.HtmlDecode(previewLink.Groups[1].Value));
        var preview = await TryGetStringAsync(previewUri, httpClient, cancellationToken);
        return preview is null ? null : TryParseMatch(PlacePreviewCoordinatePattern.Match(preview));
    }

    private static async Task<string?> TryGetStringAsync(Uri uri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<string?> ResolveRedirectsAsync(Uri startUri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var current = startUri;

        for (var hop = 0; hop < MaxRedirectHops; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch
            {
                return null;
            }

            using (response)
            {
                if (!IsRedirectStatus(response.StatusCode) || response.Headers.Location is null)
                {
                    return response.IsSuccessStatusCode ? current.ToString() : null;
                }

                current = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);
            }

            if (TryExtractFromUrlText(current.ToString()) is not null) return current.ToString();
        }

        return current.ToString();
    }

    private static bool IsRedirectStatus(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                   HttpStatusCode.Redirect or HttpStatusCode.RedirectKeepVerb or
                   HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
