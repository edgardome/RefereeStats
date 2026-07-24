using AngleSharp.Dom;
using RefereeStats.Core.Models;
using Serilog;

namespace RefereeStats.Core.Scraping;

/// <summary>Orquesta la extracción de estadísticas de árbitros por liga/temporada.</summary>
public sealed class RefereeScraper(TransfermarktClient client, ILogger logger)
{
    private const string BaseUrl = "https://www.transfermarkt.com";
    private const int MaxLeaguePages = 20; // salvaguarda: la tabla pagina de 25 en 25 filas

    /// <summary>
    /// Slug de liga (usado en la URL SEO y como <see cref="RefereeProfile.League"/>) -> código de
    /// competición de Transfermarkt (segmento .../wettbewerb/{code}/...). Son identificadores
    /// distintos: p.ej. "laliga2" (slug) vs "ES2" (código), confirmado en captura HAR real.
    /// </summary>
    private static readonly Dictionary<string, string> CompetitionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["laliga"] = "ES1",
        ["laliga2"] = "ES2",
    };

    /// <summary>Nivel 1: lista de árbitros y su agregado por temporada para una liga.</summary>
    public async Task<IReadOnlyList<RefereeProfile>> GetRefereeProfilesAsync(
        string league, string season, DateOnly? since = null, CancellationToken ct = default)
    {
        var documents = await GetLeagueDocumentsAsync(league, season, ct);
        var profiles = documents.SelectMany(d => ParseProfilesFromDocument(d, league, season)).ToList();

        logger.Information("Parsed {Count} referee profiles for {League}/{Season}", profiles.Count, league, season);

        // Incremental: si el caller pasa `since`, ese filtrado se aplica a nivel de partido (Nivel 2),
        // no de perfil agregado — se conserva el parámetro para uso futuro/consistencia de firma.
        _ = since;

        return profiles;
    }

    /// <summary>Nivel 1b: ficha biográfica de un árbitro (nacionalidad, fecha de nacimiento).</summary>
    public async Task<RefereeDetail> GetRefereeDetailAsync(string refereeId, CancellationToken ct = default)
    {
        // Transfermarkt resuelve por ID: el slug puede ser cualquier texto no vacío.
        var url = $"{BaseUrl}/-/profil/schiedsrichter/{refereeId}";
        var document = await client.GetDocumentAsync(url, ct);

        return ParseDetailFromDocument(document, refereeId);
    }

    /// <summary>
    /// Nivel 1c: desglose por temporada de un árbitro dentro de una única competición — la
    /// ficha del árbitro filtrada con `wettbewerb_id` (al entrar en una competición concreta
    /// desde su perfil).
    /// </summary>
    public async Task<IReadOnlyList<RefereeProfile>> GetRefereeCompetitionHistoryAsync(
        string refereeId, string league, CancellationToken ct = default)
    {
        var competitionCode = ResolveCompetitionCode(league);
        var url = $"{BaseUrl}/-/profil/schiedsrichter/{refereeId}/plus/0?funktion=1&saison_id=&wettbewerb_id={competitionCode}";
        var document = await client.GetDocumentAsync(url, ct);

        var history = ParseCompetitionHistoryFromDocument(document, refereeId, league);

        logger.Information("Parsed {Count} seasons for referee {RefereeId} in {League}", history.Count, refereeId, league);

        return history;
    }

    /// <summary>Descarga todas las páginas de la tabla de árbitros de una liga/temporada (paginación .../plus/N).</summary>
    private async Task<IReadOnlyList<IDocument>> GetLeagueDocumentsAsync(string league, string season, CancellationToken ct)
    {
        var competitionCode = ResolveCompetitionCode(league);
        var seasonId = SeasonToId(season);
        var documents = new List<IDocument>();

        for (var page = 0; page < MaxLeaguePages; page++)
        {
            var url = BuildLeagueUrl(league, competitionCode, seasonId, page);
            var document = await client.GetDocumentAsync(url, ct);
            if (document.QuerySelectorAll("table.items > tbody > tr").Length == 0)
            {
                break;
            }

            documents.Add(document);
        }

        return documents;
    }

    private static string BuildLeagueUrl(string league, string competitionCode, string seasonId, int page)
    {
        var url = $"{BaseUrl}/{league}/schiedsrichter/wettbewerb/{competitionCode}/saison_id/{seasonId}";
        return page == 0 ? url : $"{url}/plus/{page}";
    }

    private static string ResolveCompetitionCode(string league)
    {
        if (CompetitionCodes.TryGetValue(league, out var code))
        {
            return code;
        }

        // Ya viene como código de competición de Transfermarkt (p.ej. "ES2", "GB1").
        if (league.Length is >= 2 and <= 4 && league.All(char.IsLetterOrDigit) && league.Any(char.IsUpper))
        {
            return league;
        }

        throw new ArgumentException(
            $"Slug de liga desconocido '{league}': añade su código de competición de Transfermarkt a RefereeScraper.CompetitionCodes",
            nameof(league));
    }

    /// <summary>Parsing puro de la ficha de árbitro — testable con fixtures locales, sin red.</summary>
    public static RefereeDetail ParseDetailFromDocument(IDocument document, string refereeId)
    {
        var name = ExtractName(document);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var spans = document.QuerySelectorAll(
            "div.info-table--equal-space > span.info-table__content, div.info-table > span.info-table__content");
        for (var i = 0; i + 1 < spans.Length; i += 2)
        {
            var label = spans[i].TextContent.Trim().TrimEnd(':').Trim();
            var value = spans[i + 1].TextContent.Trim();
            fields[label] = value;
        }

        DateOnly? birthDate = null;
        if (fields.TryGetValue("Date of birth/Age", out var dob))
        {
            var datePart = dob.Split('(')[0].Trim();
            if (TryParseDateFromText(datePart, out var parsed))
            {
                birthDate = parsed;
            }
        }

        fields.TryGetValue("Citizenship", out var nationality);

        return new RefereeDetail
        {
            RefereeId = refereeId,
            Name = name,
            Nationality = string.IsNullOrWhiteSpace(nationality) ? null : nationality,
            BirthDate = birthDate,
        };
    }

    private static string ExtractName(IDocument document)
        => System.Text.RegularExpressions.Regex.Replace(
            document.QuerySelector("h1.data-header__headline-wrapper")?.TextContent ?? string.Empty, @"\s+", " ").Trim();

    /// <summary>
    /// Parsing puro del historial por temporada de un árbitro dentro de una competición
    /// (ficha filtrada por `wettbewerb_id`) — testable con fixtures locales, sin red.
    /// </summary>
    public static IReadOnlyList<RefereeProfile> ParseCompetitionHistoryFromDocument(IDocument document, string refereeId, string league)
    {
        var name = ExtractName(document);
        var history = new List<RefereeProfile>();

        foreach (var row in document.QuerySelectorAll("table.items > tbody > tr"))
        {
            var entry = TryParseCompetitionSeasonRow(row, refereeId, name, league);
            if (entry is not null)
            {
                history.Add(entry);
            }
        }

        return history;
    }

    private static RefereeProfile? TryParseCompetitionSeasonRow(IElement row, string refereeId, string name, string league)
    {
        var cells = row.QuerySelectorAll("td.zentriert").Select(c => c.TextContent.Trim()).ToArray();
        if (cells.Length < 7 || !TryNormalizeSeason(cells[0], out var season))
        {
            return null;
        }

        return new RefereeProfile
        {
            RefereeId = refereeId,
            Name = name,
            League = league,
            Season = season,
            MatchesOfficiated = ParseInt(cells[2]),
            YellowCards = ParseInt(cells[3]),
            AverageYellowCards = ParseDecimal(cells.ElementAtOrDefault(4)),
            SecondYellowCards = ParseInt(cells[5]),
            AverageSecondYellowCards = ParseDecimal(cells.ElementAtOrDefault(6)),
            DirectRedCards = ParseInt(cells[7]),
            AverageDirectRedCards = ParseDecimal(cells.ElementAtOrDefault(8)),
            PenaltiesAwarded = ParseInt(cells[9]),
            AveragePenaltiesAwarded = ParseDecimal(cells.ElementAtOrDefault(10)),
        };
    }

    private static bool TryNormalizeSeason(string raw, out string season)
    {
        // Transfermarkt abrevia la temporada en esta tabla como "25/26" -> "2025-2026"
        season = string.Empty;
        var parts = raw.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var startYy))
        {
            return false;
        }

        var startYear = startYy >= 50 ? 1900 + startYy : 2000 + startYy;
        season = $"{startYear}-{startYear + 1}";
        return true;
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

        var flagImg = row.QuerySelector("img.flaggenrahmen");
        var nationality = flagImg?.GetAttribute("alt")?.Trim();

        var allCells = row.QuerySelectorAll("td").ToArray();
        var centeredCells = row.QuerySelectorAll("td.zentriert").Select(c => c.TextContent.Trim()).ToArray();
        var rightCell = row.QuerySelector("td.rechts")?.TextContent.Trim();

        // Estructura: [0]=nombre, [1]=debut(zentriert), [2]=edad(rechts), [3]=apariciones(zentriert), [4-11]=stats
        var debutText = centeredCells.ElementAtOrDefault(0) ?? string.Empty;
        var appearances = ParseInt(centeredCells.ElementAtOrDefault(1));
        var yellowCards = ParseInt(centeredCells.ElementAtOrDefault(2));
        var avgYellow = ParseDecimal(centeredCells.ElementAtOrDefault(3));
        var secondYellow = ParseInt(centeredCells.ElementAtOrDefault(4));
        var avgSecondYellow = ParseDecimal(centeredCells.ElementAtOrDefault(5));
        var redCards = ParseInt(centeredCells.ElementAtOrDefault(6));
        var avgRed = ParseDecimal(centeredCells.ElementAtOrDefault(7));
        var penalties = ParseInt(centeredCells.ElementAtOrDefault(8));
        var avgPenalties = ParseDecimal(centeredCells.ElementAtOrDefault(9));

        return new RefereeProfile
        {
            RefereeId = refereeId,
            Name = name,
            Nationality = string.IsNullOrWhiteSpace(nationality) ? null : nationality,
            League = league,
            Season = season,
            DebutDate = TryParseDateFromText(debutText, out var debut) ? debut : null,
            MatchesOfficiated = appearances,
            YellowCards = yellowCards,
            AverageYellowCards = avgYellow,
            SecondYellowCards = secondYellow,
            AverageSecondYellowCards = avgSecondYellow,
            DirectRedCards = redCards,
            AverageDirectRedCards = avgRed,
            PenaltiesAwarded = penalties,
            AveragePenaltiesAwarded = avgPenalties,
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

    private static decimal ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "-")
        {
            return 0m;
        }
        return decimal.TryParse(raw.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private static string SeasonToId(string season)
    {
        // "2025-2026" -> "2025" (Transfermarkt identifica temporada por año de inicio)
        var year = season.Split('-')[0];
        return year;
    }

    /// <summary>
    /// Nivel 2 (NO SOPORTADO): Transfermarkt no expone ningún listado de partidos arbitrados
    /// por un árbitro concreto — investigado a fondo vía HAR (perfil, historial por
    /// competición/temporada en sus variantes "Compact"/"Detailed view"): todas esas vistas son
    /// agregados por temporada/competición (ver <see cref="GetRefereeCompetitionHistoryAsync"/>),
    /// nunca partido a partido. La página de liga (<see cref="GetLeagueDocumentsAsync"/>) tampoco
    /// sirve: lista árbitros, no partidos, así que el intento anterior de reutilizarla para esto
    /// solo generaba una paginación cara (hasta <see cref="MaxLeaguePages"/> páginas) que siempre
    /// devolvía vacío. Obtener partidos reales requeriría recorrer el calendario de la
    /// competición jornada a jornada y abrir cada spielbericht para comprobar el árbitro — fuera
    /// de alcance por ahora.
    /// </summary>
    public Task<IReadOnlyList<MatchCardEvent>> GetRefereeMatchesAsync(
        string refereeId, string league, string season, DateOnly? since = null, CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "Nivel 2 (partidos por árbitro) no soportado: Transfermarkt no expone un listado de partidos por árbitro, " +
            "solo agregados por temporada/competición. Usa GetRefereeCompetitionHistoryAsync.");
    }

    /// <summary>Extract match summaries (teams, score, match ID) from league season page.</summary>
    public static IReadOnlyList<MatchSummary> ParseMatchLinksFromDocument(IDocument document)
    {
        var summaries = new List<MatchSummary>();
        foreach (var row in document.QuerySelectorAll("table tbody > tr"))
        {
            var summary = TryParseMatchRow(row);
            if (summary is not null)
            {
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    private static MatchSummary? TryParseMatchRow(IElement row)
    {
        // Home team: tr td:nth-child(1) > a
        var homeTeamEl = row.QuerySelector("td:nth-child(1) > a");
        var homeTeam = homeTeamEl?.TextContent.Trim();

        // Score + match ID: a.ergebnis-link
        var scoreEl = row.QuerySelector("a.ergebnis-link");
        var score = scoreEl?.TextContent.Trim();
        var matchIdStr = scoreEl?.GetAttribute("id");

        // Away team: tr td:nth-child(5) > a
        var awayTeamEl = row.QuerySelector("td:nth-child(5) > a");
        var awayTeam = awayTeamEl?.TextContent.Trim();

        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(matchIdStr) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return null;
        }

        return new MatchSummary
        {
            MatchId = matchIdStr,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            FinalScore = score,
        };
    }

    /// <summary>Extract cards and date from individual match detail page.</summary>
    private static MatchCardEvent? ParseCardDataFromMatchDetail(IDocument document, MatchSummary summary, string refereeId)
    {
        var date = ExtractDateFromMatchDetail(document);
        if (date == default)
        {
            return null;
        }

        var homeCards = CountCardsInSection(document, "sb-leiste-heim");
        var awayCards = CountCardsInSection(document, "sb-leiste-gast");
        var firstCardMinute = ExtractFirstCardMinute(document);

        return new MatchCardEvent
        {
            MatchId = summary.MatchId,
            Date = date,
            HomeTeam = summary.HomeTeam,
            AwayTeam = summary.AwayTeam,
            RefereeId = refereeId,
            HomeCards = homeCards,
            AwayCards = awayCards,
            FirstCardMinute = firstCardMinute,
            FinalScore = summary.FinalScore,
        };
    }

    private static DateOnly ExtractDateFromMatchDetail(IDocument document)
    {
        // p.sb-datum contiene la jornada ("1. Matchday | ..."), no la fecha — la fecha real
        // solo aparece embebida en meta description/og:title ("... on 16/08/2025.").
        var metaDescription = document.QuerySelector("meta[name='description']")?.GetAttribute("content") ?? string.Empty;
        if (TryParseDateFromText(metaDescription, out var parsedDescription))
        {
            return parsedDescription;
        }

        var titleContent = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content") ?? string.Empty;
        if (TryParseDateFromText(titleContent, out var parsedMeta))
        {
            return parsedMeta;
        }

        return default;
    }

    private static bool TryParseDateFromText(string text, out DateOnly result)
    {
        result = default;

        // Extraer "DD/MM/YYYY" (formato europeo de Transfermarkt) embebido en frases libres,
        // p.ej. "... on 16/08/2025." — un split ingenuo por '/' rompe con ese texto alrededor.
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d{1,2})/(\d{1,2})/(\d{4})");
        if (!match.Success)
        {
            return false;
        }

        var day = int.Parse(match.Groups[1].Value);
        var month = int.Parse(match.Groups[2].Value);
        var year = int.Parse(match.Groups[3].Value);
        try
        {
            result = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    // Iconos reales del timeline del partido (div.sb-leiste-{heim,gast} > div.sb-leiste-ereignis
    // > span.sb-sprite.sb-*). "icons_sprite icon-*-formation" es del diagrama de alineación
    // (pitch view), no del timeline, y no distingue equipo de forma fiable.
    private static readonly string[] CardSpriteClasses = ["sb-gelb", "sb-gelbrot", "sb-rot"];

    private static int CountCardsInSection(IDocument document, string sectionClass)
    {
        var section = document.QuerySelector($"div.{sectionClass}");
        if (section is null) return 0;

        return section.QuerySelectorAll("div.sb-leiste-ereignis").Count(IsCardEvent);
    }

    private static int? ExtractFirstCardMinute(IDocument document)
    {
        double? earliestPercent = null;

        foreach (var eventDiv in document.QuerySelectorAll("div.sb-leiste-heim div.sb-leiste-ereignis, div.sb-leiste-gast div.sb-leiste-ereignis"))
        {
            if (!IsCardEvent(eventDiv))
            {
                continue;
            }

            var leftMatch = System.Text.RegularExpressions.Regex.Match(eventDiv.GetAttribute("style") ?? string.Empty, @"left:\s*([\d.]+)%");
            if (leftMatch.Success && double.TryParse(leftMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                earliestPercent = earliestPercent is null ? percent : Math.Min(earliestPercent.Value, percent);
            }
        }

        return earliestPercent is null ? null : (int)(earliestPercent.Value * 0.9);
    }

    // El estilo "left: X%" vive en el <div class="sb-leiste-ereignis">, no en el <span> interior.
    private static bool IsCardEvent(IElement eventDiv)
        => eventDiv.QuerySelector("span.sb-sprite") is { } sprite
            && CardSpriteClasses.Any(sprite.ClassList.Contains);
}

/// <summary>Intermediate model for match summaries extracted from league page.</summary>
public sealed class MatchSummary
{
    public required string MatchId { get; init; }
    public required string HomeTeam { get; init; }
    public required string AwayTeam { get; init; }
    public string? FinalScore { get; init; }
}
