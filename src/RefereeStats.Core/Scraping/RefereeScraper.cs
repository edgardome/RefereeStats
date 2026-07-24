using AngleSharp.Dom;
using RefereeStats.Core.Models;
using Serilog;

namespace RefereeStats.Core.Scraping;

/// <summary>Orquesta la extracción de estadísticas de árbitros por liga/temporada.</summary>
public sealed class RefereeScraper(TransfermarktClient client, ILogger logger)
{
    private const string BaseUrl = "https://www.transfermarkt.com";

    /// <summary>Nivel 1: lista de árbitros y su agregado por temporada para una liga.</summary>
    public async Task<IReadOnlyList<RefereeProfile>> GetRefereeProfilesAsync(
        string league, string season, DateOnly? since = null, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/{league}/schiedsrichter/wettbewerb/{league}/saison_id/{SeasonToId(season)}";
        var document = await client.GetDocumentAsync(url, ct);

        var profiles = ParseProfilesFromDocument(document, league, season);

        logger.Information("Parsed {Count} referee profiles for {League}/{Season}", profiles.Count, league, season);

        // Incremental: si el caller pasa `since`, ese filtrado se aplica a nivel de partido (Nivel 2),
        // no de perfil agregado — se conserva el parámetro para uso futuro/consistencia de firma.
        _ = since;

        return profiles;
    }

    /// <summary>Parsing puro sobre un documento ya cargado — testable con fixtures locales, sin red.</summary>
    public static IReadOnlyList<RefereeProfile> ParseProfilesFromDocument(IDocument document, string league, string season)
    {
        var profiles = new List<RefereeProfile>();
        foreach (var row in document.QuerySelectorAll("table.items > tbody > tr"))
        {
            var profile = TryParseRow(row, league, season);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    private static RefereeProfile? TryParseRow(IElement row, string league, string season)
    {
        var nameCell = row.QuerySelector("td.hauptlink a, td a[href*='schiedsrichter']");
        var name = nameCell?.TextContent.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var href = nameCell!.GetAttribute("href") ?? string.Empty;
        var refereeId = ExtractIdFromHref(href);
        if (refereeId is null)
        {
            return null;
        }

        var cells = row.QuerySelectorAll("td.zentriert").Select(c => c.TextContent.Trim()).ToArray();

        return new RefereeProfile
        {
            RefereeId = refereeId,
            Name = name,
            League = league,
            Season = season,
            MatchesOfficiated = ParseInt(cells.ElementAtOrDefault(0)),
            YellowCards = ParseInt(cells.ElementAtOrDefault(1)),
            SecondYellowCards = ParseInt(cells.ElementAtOrDefault(2)),
            DirectRedCards = ParseInt(cells.ElementAtOrDefault(3)),
            PenaltiesAwarded = ParseInt(cells.ElementAtOrDefault(4)),
        };
    }

    private static string? ExtractIdFromHref(string href)
    {
        // Formato típico: /nombre-apellido/profil/schiedsrichter/12345
        var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.LastOrDefault(p => p.All(char.IsDigit));
    }

    private static int ParseInt(string? raw)
        => int.TryParse(raw?.Trim(), out var value) ? value : 0;

    private static string SeasonToId(string season)
    {
        // "2025-2026" -> "2025" (Transfermarkt identifica temporada por año de inicio)
        var year = season.Split('-')[0];
        return year;
    }
}
