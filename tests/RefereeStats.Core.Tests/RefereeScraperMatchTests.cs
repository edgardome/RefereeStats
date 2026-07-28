using AngleSharp;
using AngleSharp.Html.Parser;
using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;

namespace RefereeStats.Core.Tests;

public class RefereeScraperMatchTests
{
    private static async Task<AngleSharp.Dom.IDocument> LoadFixtureAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        var html = await File.ReadAllTextAsync(path);
        var parser = new HtmlParser();
        return await parser.ParseDocumentAsync(html);
    }

    [Fact]
    public async Task ParseGranularMatchReport_ExtractsDateCorrectly()
    {
        var doc = await LoadFixtureAsync("granular-match-detail.html");
        
        var matchEvent = RefereeScraper.ParseGranularMatchReport(doc, "12345");

        Assert.NotNull(matchEvent);
        Assert.Equal(new DateOnly(2025, 5, 26), matchEvent.Date);
    }

    [Fact]
    public async Task ParseGranularMatchReport_ExtractsCardsCorrectly()
    {
        var doc = await LoadFixtureAsync("granular-match-detail.html");
        
        var matchEvent = RefereeScraper.ParseGranularMatchReport(doc, "12345");

        Assert.NotNull(matchEvent);
        // We assume the fixture has a specific number of cards, we just check it doesn't crash
        Assert.NotNull(matchEvent.Cards);
        Assert.Equal(2, matchEvent.Cards.Count);
    }

    [Fact]
    public async Task ParseGranularMatchReport_PreservesTeamsAndScore()
    {
        var doc = await LoadFixtureAsync("granular-match-detail.html");
        
        var matchEvent = RefereeScraper.ParseGranularMatchReport(doc, "12345");

        Assert.NotNull(matchEvent);
        Assert.Equal("Barcelona", matchEvent.HomeTeam);
        Assert.Equal("Real Madrid", matchEvent.AwayTeam);
        Assert.Equal("2:1", matchEvent.FinalScore);
    }
}
