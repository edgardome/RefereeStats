using System.Net;
using RefereeStats.Core.Scraping;
using Serilog;

namespace RefereeStats.Core.Tests;

public class TransfermarktClientTests
{
    private static TransfermarktClient BuildClient(HttpStatusCode statusCode, string content)
    {
        var handler = new FakeHttpMessageHandler(statusCode, content);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.transfermarkt.com") };

        // Permissive robots.txt with zero crawl-delay so tests run fast.
        var robotsHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, "User-agent: *\nCrawl-delay: 0\n");
        var robotsHttpClient = new HttpClient(robotsHandler) { BaseAddress = new Uri("https://www.transfermarkt.com") };
        var robotsTxt = new RobotsTxtChecker(robotsHttpClient, Log.Logger);

        return new TransfermarktClient(httpClient, robotsTxt, Log.Logger);
    }

    private static async Task<string> ReadFixtureAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return await File.ReadAllTextAsync(path);
    }

    [Fact]
    public async Task DetectsSilentFailure_OnChallengePage()
    {
        var html = await ReadFixtureAsync("challenge-page.html");
        var client = BuildClient(HttpStatusCode.OK, html);

        var ex = await Assert.ThrowsAsync<ScrapeException>(() => client.GetDocumentAsync("/schiedsrichter"));

        Assert.Equal(ErrorCategory.SilentFailure, ex.Category);
    }

    [Fact]
    public async Task ThrowsRateLimited_On429()
    {
        var client = BuildClient(HttpStatusCode.TooManyRequests, string.Empty);

        var ex = await Assert.ThrowsAsync<ScrapeException>(() => client.GetDocumentAsync("/schiedsrichter"));

        Assert.Equal(ErrorCategory.RateLimited, ex.Category);
    }

    [Fact]
    public async Task ReturnsDocument_ForValidPage()
    {
        var html = await ReadFixtureAsync("referee-list-valid.html");
        var client = BuildClient(HttpStatusCode.OK, html);

        var document = await client.GetDocumentAsync("/schiedsrichter");

        Assert.NotNull(document.QuerySelector("table.items"));
    }
}
