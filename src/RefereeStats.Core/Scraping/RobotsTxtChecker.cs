using Serilog;

namespace RefereeStats.Core.Scraping;

/// <summary>
/// Minimal robots.txt compliance: fetches once, caches Disallow rules for the
/// default (*) user-agent, and honors Crawl-delay if present.
/// </summary>
public sealed class RobotsTxtChecker(HttpClient httpClient, ILogger logger)
{
    private List<string>? _disallowedPaths;
    private TimeSpan? _crawlDelay;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public async Task<bool> IsAllowedAsync(string path, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _disallowedPaths is null || !_disallowedPaths.Any(disallowed =>
            disallowed.Length > 0 && path.StartsWith(disallowed, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<TimeSpan> GetCrawlDelayAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _crawlDelay ?? TimeSpan.FromSeconds(1);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_disallowedPaths is not null)
        {
            return;
        }

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_disallowedPaths is not null)
            {
                return;
            }

            _disallowedPaths = [];

            try
            {
                var response = await httpClient.GetAsync("/robots.txt", ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.Information("robots.txt not available ({Status}), proceeding with default politeness", response.StatusCode);
                    return;
                }

                var text = await response.Content.ReadAsStringAsync(ct);
                ParseRobotsTxt(text);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to fetch robots.txt, proceeding with default politeness");
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void ParseRobotsTxt(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var applicableToUs = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') || line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var directive = parts[0].Trim().ToLowerInvariant();
            var value = parts[1].Trim();

            switch (directive)
            {
                case "user-agent":
                    applicableToUs = value == "*";
                    break;
                case "disallow" when applicableToUs && value.Length > 0:
                    _disallowedPaths!.Add(value);
                    break;
                case "crawl-delay" when applicableToUs && double.TryParse(value, out var seconds):
                    _crawlDelay = TimeSpan.FromSeconds(seconds);
                    break;
            }
        }

        logger.Information("robots.txt loaded: {Count} disallow rules, crawl-delay={Delay}s",
            _disallowedPaths!.Count, _crawlDelay?.TotalSeconds ?? 1);
    }
}
