using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Serilog;

namespace RefereeStats.Core.Scraping;

/// <summary>
/// Cliente HTTP hacia Transfermarkt. Sin navegador headless: HttpClient + AngleSharp.
/// Detecta bloqueos, rate-limiting y fallos silenciosos (HTTP 200 con página de
/// verificación/CAPTCHA en vez del contenido esperado).
/// </summary>
public sealed class TransfermarktClient(HttpClient httpClient, ILogger logger)
{
    private const string ExpectedContentSelector = "#main"; // selector presente en páginas válidas de perfil/liga
    private static readonly IHtmlParser Parser = new HtmlParser();

    public async Task<IDocument> GetDocumentAsync(string relativeOrAbsoluteUrl, CancellationToken ct = default)
    {
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
            logger.Warning("Rate limited fetching {Url}", relativeOrAbsoluteUrl);
            throw new ScrapeException(ErrorCategory.RateLimited, $"Rate limited fetching {relativeOrAbsoluteUrl}");
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable)
        {
            logger.Warning("Blocked fetching {Url} ({Status})", relativeOrAbsoluteUrl, response.StatusCode);
            throw new ScrapeException(ErrorCategory.Blocked, $"Blocked fetching {relativeOrAbsoluteUrl} ({response.StatusCode})");
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
