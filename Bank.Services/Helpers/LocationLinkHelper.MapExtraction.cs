using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Bank.Services.Helpers;

// partial عشان الكلاس الأصلي LocationLinkHelper (فيه ToPoint/ToGoogleMapsLink)
// موجود بالفعل وما عايزينش نلمسه — ده بس بيضيف استخراج الإحداثيات من اللينكات.
public static partial class LocationLinkHelper
{
    private static readonly Regex CoordinatePattern = new(
        @"(?:destination=|[?&]q=|[?&]ll=|@)(-?\d{1,3}\.\d+),(-?\d{1,3}\.\d+)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ShortLinkHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "maps.app.goo.gl", "goo.gl"
    };

    private const int MaxRedirectHops = 5;

    /// <summary>
    /// بيحاول يستخرج (Latitude, Longitude) من رابط خرائط جوجل — سواء كان فيه
    /// الإحداثيات مباشرة، أو رابط مختصر (maps.app.goo.gl/goo.gl) بيتابعه عبر
    /// الـ redirects لحد ما يلاقي رابط فيه إحداثيات أو يفشل.
    /// </summary>
    public static async Task<(double Latitude, double Longitude)?> TryExtractCoordinatesAsync(
        string? url, HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var direct = TryExtractFromUrlText(url);
        if (direct.HasValue) return direct;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!ShortLinkHosts.Contains(uri.Host)) return null;

        var resolvedUrl = await ResolveRedirectsAsync(uri, httpClient, cancellationToken);
        return resolvedUrl is null ? null : TryExtractFromUrlText(resolvedUrl);
    }

    /// <summary>
    /// نسخة Batch — بتعمل Distinct على اللينكات وتحل كل لينك فريد مرة واحدة بس.
    /// المفتاح في الـ Dictionary الراجعة هو الرابط الأصلي كما هو.
    /// </summary>
    public static async Task<Dictionary<string, (double Latitude, double Longitude)?>> ExtractCoordinatesBatchAsync(
        IEnumerable<string?> urls, HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        var distinctUrls = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
        var result = new Dictionary<string, (double, double)?>();

        foreach (var url in distinctUrls)
        {
            result[url!] = await TryExtractCoordinatesAsync(url, httpClient, cancellationToken);
        }

        return result;
    }

    private static (double, double)? TryExtractFromUrlText(string url)
    {
        var match = CoordinatePattern.Match(url);
        if (!match.Success) return null;

        if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        {
            return (lat, lng);
        }

        return null;
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

            if (!IsRedirectStatus(response.StatusCode) || response.Headers.Location is null)
            {
                return response.IsSuccessStatusCode ? current.ToString() : null;
            }

            current = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current, response.Headers.Location);

            if (TryExtractFromUrlText(current.ToString()) is not null) return current.ToString();
        }

        return current.ToString();
    }

    private static bool IsRedirectStatus(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                   HttpStatusCode.Redirect or HttpStatusCode.RedirectKeepVerb or
                   HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
