using AngleSharp.Html.Parser;
using RefereeStats.Core.Scraping;

namespace RefereeStats.Core.Tests;

public class RefereeScraperCompetitionHistoryTests
{
    private static async Task<AngleSharp.Dom.IDocument> LoadFixtureAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        var html = await File.ReadAllTextAsync(path);
        var parser = new HtmlParser();
        return await parser.ParseDocumentAsync(html);
    }

    [Fact]
    public async Task ParsesOneRowPerSeason()
    {
        var document = await LoadFixtureAsync("referee-competition-history-valid.html");

        var history = RefereeScraper.ParseCompetitionHistoryFromDocument(document, "9479", "laliga2");

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task ParsesSeasonFieldsAndNormalizesSeasonFormat()
    {
        var document = await LoadFixtureAsync("referee-competition-history-valid.html");

        var history = RefereeScraper.ParseCompetitionHistoryFromDocument(document, "9479", "laliga2");
        var recent = history.Single(h => h.Season == "2025-2026");

        Assert.Equal("9479", recent.RefereeId);
        Assert.Equal("Saúl Ais Reig", recent.Name);
        Assert.Equal("laliga2", recent.League);
        Assert.Equal(22, recent.MatchesOfficiated);
        Assert.Equal(103, recent.YellowCards);
        Assert.Equal(1, recent.SecondYellowCards);
        Assert.Equal(3, recent.DirectRedCards);
        Assert.Equal(2, recent.PenaltiesAwarded);

        var old = history.Single(h => h.Season == "2017-2018");
        Assert.Equal(76, old.YellowCards);
    }
}
