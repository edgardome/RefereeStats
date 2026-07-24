using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Serilog;

namespace RefereeStats.Core.Scraping;

/// <summary>
/// Cliente HTTP hacia Transfermarkt. Sin navegador headless: HttpClient + AngleSharp.
/// Respeta robots.txt (Disallow + Crawl-delay), aplica una demora de cortesía entre
/// peticiones, y detecta bloqueos, rate-limiting y fallos silenciosos (HTTP 200 con
/// página de verificación/CAPTCHA en vez del contenido esperado).
/// </summary>
public sealed class TransfermarktClient(HttpClient httpClient, RobotsTxtChecker robotsTxt, ILogger logger)
{
    private const string ExpectedContentSelector = "#main"; // selector presente en páginas válidas de perfil/liga
    private static readonly IHtmlParser Parser = new HtmlParser();
    private readonly SemaphoreSlim _politenessLock = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public async Task<IDocument> GetDocumentAsync(string relativeOrAbsoluteUrl, CancellationToken ct = default)
    {
        var path = new Uri(httpClient.BaseAddress!, relativeOrAbsoluteUrl).AbsolutePath;
        if (!await robotsTxt.IsAllowedAsync(path, ct))
        {
            logger.Warning("robots.txt disallows {Path}, skipping", path);
            throw new ScrapeException(ErrorCategory.Blocked, $"robots.txt disallows {path}");
        }

        // Transfermarkt a veces responde con un 302 cuya Location apunta a la misma
        // URL: solo pretende fijar una cookie (anti-bot) antes de servir el contenido.
        // .NET no sigue automáticamente un auto-redirect, así que reintentamos una vez
        // con la cookie ya almacenada en el CookieContainer compartido.
        for (var attempt = 0; ; attempt++)
        {
            var response = await FetchAsync(relativeOrAbsoluteUrl, ct);

            if (IsSelfRedirect(response, relativeOrAbsoluteUrl) && attempt == 0)
            {
                logger.Information("Self-redirect (cookie bounce) fetching {Url}, retrying with stored cookie", relativeOrAbsoluteUrl);
                continue;
            }

            return await ParseResponseAsync(response, relativeOrAbsoluteUrl, ct);
        }
    }

    private async Task<HttpResponseMessage> FetchAsync(string relativeOrAbsoluteUrl, CancellationToken ct)
    {
        await ApplyPolitenessDelayAsync(ct);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(relativeOrAbsoluteUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.Error(ex, "Network error fetching {Url}", relativeOrAbsoluteUrl);
            throw new ScrapeException(ErrorCategory.NetworkError, $"Network error fetching {relativeOrAbsoluteUrl}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.Error(ex, "Timeout fetching {Url}", relativeOrAbsoluteUrl);
            throw new ScrapeException(ErrorCategory.NetworkError, $"Timeout fetching {relativeOrAbsoluteUrl}", ex);
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            logger.Warning("Rate limited fetching {Url}, Retry-After={RetryAfter}s", relativeOrAbsoluteUrl, retryAfter.TotalSeconds);
            throw new ScrapeException(ErrorCategory.RateLimited, $"Rate limited fetching {relativeOrAbsoluteUrl}, retry after {retryAfter.TotalSeconds}s");
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable)
        {
            logger.Warning("Blocked fetching {Url} ({Status})", relativeOrAbsoluteUrl, response.StatusCode);
            throw new ScrapeException(ErrorCategory.Blocked, $"Blocked fetching {relativeOrAbsoluteUrl} ({response.StatusCode})");
        }

        return response;
    }

    /// <summary>
    /// Un 302/301 cuya Location resuelve a la misma URL solicitada es un "cookie
    /// bounce": el servidor fija una cookie y espera que el cliente repita la
    /// petición, no un fallo real.
    /// </summary>
    private bool IsSelfRedirect(HttpResponseMessage response, string relativeOrAbsoluteUrl)
    {
        if ((int)response.StatusCode != 302 && (int)response.StatusCode != 301)
        {
            return false;
        }

        var location = response.Headers.Location;
        if (location is null)
        {
            return false;
        }

        var requestedUri = new Uri(httpClient.BaseAddress!, relativeOrAbsoluteUrl);
        var redirectUri = location.IsAbsoluteUri ? location : new Uri(httpClient.BaseAddress!, location);
        return Uri.Compare(requestedUri, redirectUri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private async Task<IDocument> ParseResponseAsync(HttpResponseMessage response, string relativeOrAbsoluteUrl, CancellationToken ct)
    {
        // Detect redirect loops (bad Location header with repeated segments)
        if ((int)response.StatusCode == 302 || (int)response.StatusCode == 301)
        {
            var location = response.Headers.Location?.OriginalString ?? "";
            if (location.Contains("wettbewerbe/wettbewerbe") || location.Length > 500)
            {
                logger.Warning("Redirect loop detected fetching {Url}, location: {Location}", relativeOrAbsoluteUrl, location);
                throw new ScrapeException(ErrorCategory.Blocked, $"Redirect loop detected fetching {relativeOrAbsoluteUrl}");
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.Warning("Unexpected status {Status} fetching {Url}", response.StatusCode, relativeOrAbsoluteUrl);
            throw new ScrapeException(ErrorCategory.NetworkError, $"HTTP {(int)response.StatusCode} fetching {relativeOrAbsoluteUrl}");
        }

        var html = await response.Content.ReadAsStringAsync(ct);

        IDocument document;
        try
        {
            document = await Parser.ParseDocumentAsync(html, ct);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Parse error fetching {Url}", relativeOrAbsoluteUrl);
            throw new ScrapeException(ErrorCategory.ParseError, $"Failed to parse HTML from {relativeOrAbsoluteUrl}", ex);
        }

        DetectSilentFailure(document, relativeOrAbsoluteUrl);

        return document;
    }

    /// <summary>
    /// Enforces a minimum gap between consecutive requests (robots.txt Crawl-delay,
    /// or 1s default) so we don't hammer the site — single-threaded politeness.
    /// </summary>
    private async Task ApplyPolitenessDelayAsync(CancellationToken ct)
    {
        var crawlDelay = await robotsTxt.GetCrawlDelayAsync(ct);

        await _politenessLock.WaitAsync(ct);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
            var waitTime = crawlDelay - elapsed;
            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime, ct);
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _politenessLock.Release();
        }
    }

    /// <summary>
    /// Transfermarkt (y CDNs delante de él) a veces devuelven 200 OK con una página
    /// de verificación humana/CAPTCHA en vez del contenido real. Si faltan los
    /// selectores clave esperados, se trata como fallo silencioso.
    /// </summary>
    private void DetectSilentFailure(IDocument document, string url)
    {
        var title = document.Title ?? string.Empty;
        var looksLikeChallenge = title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase)
            || document.QuerySelector("#challenge-form") is not null;

        if (looksLikeChallenge)
        {
            logger.Warning("Silent failure (challenge page) fetching {Url}", url);
            throw new ScrapeException(ErrorCategory.SilentFailure, $"Challenge/verification page returned for {url}");
        }

        if (document.QuerySelector(ExpectedContentSelector) is null)
        {
            logger.Warning("Silent failure (missing expected content) fetching {Url}", url);
            throw new ScrapeException(ErrorCategory.SilentFailure, $"Expected content selector '{ExpectedContentSelector}' missing at {url}");
        }
    }
}
