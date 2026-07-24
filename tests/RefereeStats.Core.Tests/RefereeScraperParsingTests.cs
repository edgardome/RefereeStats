using AngleSharp;
using AngleSharp.Html.Parser;
using RefereeStats.Core.Scraping;

namespace RefereeStats.Core.Tests;

public class RefereeScraperParsingTests
{
    private static async Task<AngleSharp.Dom.IDocument> LoadFixtureAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        var html = await File.ReadAllTextAsync(path);
        var parser = new HtmlParser();
        return await parser.ParseDocumentAsync(html);
    }

    [Fact]
    public async Task ParsesAllRefereesFromValidFixture()
    {
        var document = await LoadFixtureAsync("referee-list-valid.html");

        var profiles = RefereeScraper.ParseProfilesFromDocument(document, "laliga2", "2025-2026");

        Assert.Equal(2, profiles.Count);
    }

    [Fact]
    public async Task ParsesRefereeFieldsCorrectly()
    {
        var document = await LoadFixtureAsync("referee-list-valid.html");

        var profiles = RefereeScraper.ParseProfilesFromDocument(document, "laliga2", "2025-2026");
        var juan = profiles.Single(p => p.RefereeId == "12345");

        Assert.Equal("Juan Pérez", juan.Name);
        Assert.Equal("laliga2", juan.League);
        Assert.Equal("2025-2026", juan.Season);
        Assert.Equal(18, juan.MatchesOfficiated);
        Assert.Equal(62, juan.YellowCards);
        Assert.Equal(3, juan.SecondYellowCards);
        Assert.Equal(2, juan.DirectRedCards);
        Assert.Equal(5, juan.PenaltiesAwarded);
    }

    [Fact]
    public async Task CalculatesCardsPerMatch()
    {
        var document = await LoadFixtureAsync("referee-list-valid.html");

        var profiles = RefereeScraper.ParseProfilesFromDocument(document, "laliga2", "2025-2026");
        var juan = profiles.Single(p => p.RefereeId == "12345");

        // (62 + 3 + 2) / 18 = 3.72
        Assert.Equal(3.72m, juan.CardsPerMatch);
    }

    [Fact]
    public async Task ReturnsEmptyList_WhenLeagueHasNoRefereeRows()
    {
        var document = await LoadFixtureAsync("referee-list-empty.html");

        var profiles = RefereeScraper.ParseProfilesFromDocument(document, "laliga2", "2025-2026");

        Assert.Empty(profiles);
    }
}
