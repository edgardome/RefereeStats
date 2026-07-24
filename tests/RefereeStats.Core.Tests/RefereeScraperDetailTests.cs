using AngleSharp.Html.Parser;
using RefereeStats.Core.Scraping;

namespace RefereeStats.Core.Tests;

public class RefereeScraperDetailTests
{
    private static async Task<AngleSharp.Dom.IDocument> LoadFixtureAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        var html = await File.ReadAllTextAsync(path);
        var parser = new HtmlParser();
        return await parser.ParseDocumentAsync(html);
    }

    [Fact]
    public async Task ParsesNameNationalityAndBirthDate()
    {
        var document = await LoadFixtureAsync("referee-profile-valid.html");

        var detail = RefereeScraper.ParseDetailFromDocument(document, "5706");

        Assert.Equal("5706", detail.RefereeId);
        Assert.Equal("Dámaso Arcediano Monescillo", detail.Name);
        Assert.Equal("Spain", detail.Nationality);
        Assert.Equal(new DateOnly(1984, 9, 20), detail.BirthDate);
    }
}
