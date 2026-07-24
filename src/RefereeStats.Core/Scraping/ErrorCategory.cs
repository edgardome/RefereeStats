namespace RefereeStats.Core.Scraping;

public enum ErrorCategory
{
    None,
    Blocked,
    RateLimited,
    NetworkError,
    ParseError,
    SilentFailure,
}

public sealed class ScrapeException(ErrorCategory category, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ErrorCategory Category { get; } = category;
}
