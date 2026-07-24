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
    public async Task ParseCardDataFromMatchDetail_ExtractsDateCorrectly()
    {
        var doc = await LoadFixtureAsync("referee-match-detail.html");
        var summary = new MatchSummary
        {
            MatchId = "12345",
            HomeTeam = "Barcelona",
            AwayTeam = "Real Madrid",
            FinalScore = "2:1",
        };

        // Call private method via reflection (or make internal/public for testing)
        var matchEvent = ExtractMatchCardEvent(doc, summary, "98765");

        Assert.NotNull(matchEvent);
        Assert.Equal(new DateOnly(2025, 5, 26), matchEvent.Date);
    }

    [Fact]
    public async Task ParseCardDataFromMatchDetail_CountsCardsCorrectly()
    {
        var doc = await LoadFixtureAsync("referee-match-detail.html");
        var summary = new MatchSummary
        {
            MatchId = "12345",
            HomeTeam = "Barcelona",
            AwayTeam = "Real Madrid",
            FinalScore = "2:1",
        };

        var matchEvent = ExtractMatchCardEvent(doc, summary, "98765");

        Assert.Equal(3, matchEvent.HomeCards); // 2 yellow + 1 red
        Assert.Equal(1, matchEvent.AwayCards); // 1 yellow
    }

    [Fact]
    public async Task ParseCardDataFromMatchDetail_ExtractsFirstCardMinute()
    {
        var doc = await LoadFixtureAsync("referee-match-detail.html");
        var summary = new MatchSummary
        {
            MatchId = "12345",
            HomeTeam = "Barcelona",
            AwayTeam = "Real Madrid",
            FinalScore = "2:1",
        };

        var matchEvent = ExtractMatchCardEvent(doc, summary, "98765");

        // 16.67% of 90 minutes ≈ 15 minutes
        // TODO: integration test via controller endpoint
        Assert.Equal(15, matchEvent.FirstCardMinute);
    }

    [Fact]
    public async Task ParseCardDataFromMatchDetail_PreservesTeamsAndScore()
    {
        var doc = await LoadFixtureAsync("referee-match-detail.html");
        var summary = new MatchSummary
        {
            MatchId = "54321",
            HomeTeam = "Atletico Madrid",
            AwayTeam = "Valencia",
            FinalScore = "1:0",
        };

        var matchEvent = ExtractMatchCardEvent(doc, summary, "11111");

        Assert.Equal("Atletico Madrid", matchEvent.HomeTeam);
        Assert.Equal("Valencia", matchEvent.AwayTeam);
        Assert.Equal("1:0", matchEvent.FinalScore);
        Assert.Equal("54321", matchEvent.MatchId);
        Assert.Equal("11111", matchEvent.RefereeId);
    }

    // Helper to call the private parsing method
    private static MatchCardEvent? ExtractMatchCardEvent(
        AngleSharp.Dom.IDocument document,
        MatchSummary summary,
        string refereeId)
    {
        // Reflection call to private method
        var method = typeof(RefereeScraper).GetMethod(
            "ParseCardDataFromMatchDetail",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            null,
            [typeof(AngleSharp.Dom.IDocument), typeof(MatchSummary), typeof(string)],
            null);

        var result = method?.Invoke(null, [document, summary, refereeId]);
        return result as MatchCardEvent;
    }
}
