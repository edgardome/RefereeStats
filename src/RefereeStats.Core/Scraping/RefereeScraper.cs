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
        ["premier"] = "GB1",
        ["championship"] = "GB2",
        ["primera-federacion-grupo-i"] = "E3G1",
        ["primera-federacion-grupo-ii"] = "E3G2",
        ["segunda-federacion-grupo-i"] = "E4G1",
        ["segunda-federacion-grupo-ii"] = "E4G2",
        ["segunda-federacion-grupo-iii"] = "E4G3",
        ["segunda-federacion-grupo-iv"] = "E4G4",
        ["segunda-federacion-grupo-v"] = "E4G5",
        ["copa-del-rey"] = "CDR",
        ["copa-federacion"] = "CEF1",
        ["primera-division-argentina"] = "ARGC",
        ["bundesliga"] = "L1",
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
        var seenRefereeIds = new HashSet<string>();

        for (var page = 1; page <= MaxLeaguePages; page++)
        {
            var url = BuildLeagueUrl(league, competitionCode, seasonId, page);
            var document = await client.GetDocumentAsync(url, ct);
            
            var rows = document.QuerySelectorAll("table.items > tbody > tr");
            if (rows.Length == 0)
            {
                break;
            }

            var pageRefereeIds = new List<string>();
            foreach (var row in rows)
            {
                var link = row.QuerySelector("td.hauptlink a");
                var href = link?.GetAttribute("href") ?? string.Empty;
                var id = ExtractIdFromHref(href);
                if (!string.IsNullOrEmpty(id))
                {
                    pageRefereeIds.Add(id);
                }
            }

            // If we have already seen all referee IDs on this page, we've wrapped around or hit a duplicate page.
            if (page > 1 && pageRefereeIds.Count > 0 && pageRefereeIds.All(seenRefereeIds.Contains))
            {
                break;
            }

            foreach (var id in pageRefereeIds)
            {
                seenRefereeIds.Add(id);
            }

            documents.Add(document);
        }

        return documents;
    }

    private static string BuildLeagueUrl(string league, string competitionCode, string seasonId, int page)
    {
        var isCup = competitionCode == "CDR" || competitionCode == "CEF1";
        var compType = isCup ? "pokalwettbewerb" : "wettbewerb";
        var url = $"{BaseUrl}/{league}/schiedsrichter/{compType}/{competitionCode}/saison_id/{seasonId}/plus/1";
        return page == 1 ? url : $"{url}/page/{page}";
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

        int matchesOfficiated = 0, yellowCards = 0, secondYellowCards = 0, directRedCards = 0, penaltiesAwarded = 0;
        decimal avgYellow = 0m, avgSecondYellow = 0m, avgRed = 0m, avgPenalties = 0m;

        if (cells.Length >= 11)
        {
            matchesOfficiated = ParseInt(cells[2]);
            yellowCards = ParseInt(cells[3]);
            avgYellow = ParseDecimal(cells.ElementAtOrDefault(4));
            secondYellowCards = ParseInt(cells[5]);
            avgSecondYellow = ParseDecimal(cells.ElementAtOrDefault(6));
            directRedCards = ParseInt(cells[7]);
            avgRed = ParseDecimal(cells.ElementAtOrDefault(8));
            penaltiesAwarded = ParseInt(cells[9]);
            avgPenalties = ParseDecimal(cells.ElementAtOrDefault(10));
        }
        else
        {
            // Compact view
            matchesOfficiated = ParseInt(cells[2]);
            yellowCards = ParseInt(cells[3]);
            secondYellowCards = ParseInt(cells[4]);
            directRedCards = ParseInt(cells[5]);
            penaltiesAwarded = ParseInt(cells[6]);

            if (matchesOfficiated > 0)
            {
                avgYellow = Math.Round((decimal)yellowCards / matchesOfficiated, 2);
                avgSecondYellow = Math.Round((decimal)secondYellowCards / matchesOfficiated, 2);
                avgRed = Math.Round((decimal)directRedCards / matchesOfficiated, 2);
                avgPenalties = Math.Round((decimal)penaltiesAwarded / matchesOfficiated, 2);
            }
        }

        return new RefereeProfile
        {
            RefereeId = refereeId,
            Name = name,
            League = league,
            Season = season,
            MatchesOfficiated = matchesOfficiated,
            YellowCards = yellowCards,
            AverageYellowCards = avgYellow,
            SecondYellowCards = secondYellowCards,
            AverageSecondYellowCards = avgSecondYellow,
            DirectRedCards = directRedCards,
            AverageDirectRedCards = avgRed,
            PenaltiesAwarded = penaltiesAwarded,
            AveragePenaltiesAwarded = avgPenalties,
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

        var debutText = centeredCells.ElementAtOrDefault(0) ?? string.Empty;
        var appearances = ParseInt(centeredCells.ElementAtOrDefault(1));

        int yellowCards = 0, secondYellow = 0, redCards = 0, penalties = 0;
        decimal avgYellow = 0m, avgSecondYellow = 0m, avgRed = 0m, avgPenalties = 0m;

        if (centeredCells.Length >= 10)
        {
            // Detailed View
            yellowCards = ParseInt(centeredCells.ElementAtOrDefault(2));
            avgYellow = ParseDecimal(centeredCells.ElementAtOrDefault(3));
            secondYellow = ParseInt(centeredCells.ElementAtOrDefault(4));
            avgSecondYellow = ParseDecimal(centeredCells.ElementAtOrDefault(5));
            redCards = ParseInt(centeredCells.ElementAtOrDefault(6));
            avgRed = ParseDecimal(centeredCells.ElementAtOrDefault(7));
            penalties = ParseInt(centeredCells.ElementAtOrDefault(8));
            avgPenalties = ParseDecimal(centeredCells.ElementAtOrDefault(9));
        }
        else
        {
            // Compact View
            yellowCards = ParseInt(centeredCells.ElementAtOrDefault(2));
            secondYellow = ParseInt(centeredCells.ElementAtOrDefault(3));
            redCards = ParseInt(centeredCells.ElementAtOrDefault(4));
            penalties = ParseInt(centeredCells.ElementAtOrDefault(5));

            if (appearances > 0)
            {
                avgYellow = Math.Round((decimal)yellowCards / appearances, 2);
                avgSecondYellow = Math.Round((decimal)secondYellow / appearances, 2);
                avgRed = Math.Round((decimal)redCards / appearances, 2);
                avgPenalties = Math.Round((decimal)penalties / appearances, 2);
            }
        }

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
    /// Scrape the entire matchday calendar for a league season to get all Match IDs.
    /// Example URL: /laliga/gesamtspielplan/wettbewerb/ES1/saison_id/2024
    /// </summary>
    public async Task<IReadOnlyList<string>> GetMatchdayCalendarAsync(string league, string season, CancellationToken ct = default)
    {
        var competitionCode = ResolveCompetitionCode(league);
        var isCup = competitionCode == "CDR" || competitionCode == "CEF1";
        var compType = isCup ? "pokalwettbewerb" : "wettbewerb";
        var url = $"/{league}/gesamtspielplan/{compType}/{competitionCode}/saison_id/{SeasonToId(season)}";
        var document = await client.GetDocumentAsync(url, ct);

        var summaries = ParseMatchLinksFromDocument(document);
        
        // Filter out future/unplayed matches (which don't have a valid score like '1:2')
        return summaries
            .Where(s => s.FinalScore != null && System.Text.RegularExpressions.Regex.IsMatch(s.FinalScore.Trim(), @"^\d+:\d+$"))
            .Select(s => s.MatchId)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Scrapes details for a single match report.
    /// </summary>
    public async Task<GranularMatchReport?> GetSingleGranularMatchReportAsync(string matchId, CancellationToken ct = default)
    {
        var url = $"/spielbericht/index/spielbericht/{matchId}";
        var document = await client.GetDocumentAsync(url, ct);
        return ParseGranularMatchReport(document, matchId);
    }

    /// <summary>
    /// Orchestrates fetching granular match reports for a list of match IDs concurrently.
    /// This respects the client's politeness delays.
    /// </summary>
    public async Task<IReadOnlyList<GranularMatchReport>> GetGranularMatchReportsAsync(IReadOnlyList<string> matchIds, CancellationToken ct = default)
    {
        var reports = new List<GranularMatchReport>();
        
        foreach (var matchId in matchIds)
        {
            if (ct.IsCancellationRequested) break;
            
            try
            {
                var report = await GetSingleGranularMatchReportAsync(matchId, ct);
                
                if (report != null)
                {
                    reports.Add(report);
                }
            }
            catch (Exception ex)
            {
                // Continue scraping other matches even if one fails
                logger.Warning(ex, "Failed to scrape match report for ID {MatchId}", matchId);
            }
        }

        return reports;
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
        // Home team: td with classes 'text-right' and 'hauptlink' containing an <a> tag
        var homeTeamEl = row.QuerySelector("td.hauptlink.text-right a, td.text-right.hauptlink a");
        var homeTeam = homeTeamEl?.TextContent.Trim();

        // Score + match ID: a.ergebnis-link
        var scoreEl = row.QuerySelector("a.ergebnis-link");
        var score = scoreEl?.TextContent.Trim();
        var href = scoreEl?.GetAttribute("href");
        var matchIdStr = href != null ? href.Split('/').LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) : null;

        // Away team: td with classes 'no-border-links' and 'hauptlink' containing an <a> tag
        var awayTeamEl = row.QuerySelector("td.hauptlink.no-border-links a, td.no-border-links.hauptlink a");
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

    /// <summary>Extract comprehensive match report including granular card events and referee info.</summary>
    public static GranularMatchReport? ParseGranularMatchReport(IDocument document, string matchId)
    {
        var date = ExtractDateFromMatchDetail(document);
        if (date == default)
        {
            // Return a fallback so we don't repeatedly scrape this unparseable match page
            return new GranularMatchReport
            {
                MatchId = matchId,
                RefereeId = "unknown",
                Date = default,
                HomeTeam = "Unknown",
                AwayTeam = "Unknown",
                FinalScore = null
            };
        }

        var refereeId = ExtractRefereeIdFromMatchDetail(document);
        
        var homeTeamEl = document.QuerySelector("div.sb-heim a.sb-vereinslink");
        var awayTeamEl = document.QuerySelector("div.sb-gast a.sb-vereinslink");
        var homeTeamName = homeTeamEl?.TextContent.Trim() ?? "Unknown";
        var awayTeamName = awayTeamEl?.TextContent.Trim() ?? "Unknown";

        var scoreEl = document.QuerySelector("div.sb-endstand");
        var scoreText = scoreEl?.TextContent.Split('(').FirstOrDefault()?.Trim(); // "0:2(0:0)" -> "0:2"

        if (string.IsNullOrWhiteSpace(refereeId))
        {
            // Return fallback with "unknown" referee to prevent rescraping matches with no referee details
            return new GranularMatchReport
            {
                MatchId = matchId,
                RefereeId = "unknown",
                Date = date,
                HomeTeam = homeTeamName,
                AwayTeam = awayTeamName,
                FinalScore = scoreText
            };
        }

        return new GranularMatchReport
        {
            MatchId = matchId,
            RefereeId = refereeId,
            Date = date,
            HomeTeam = homeTeamName,
            AwayTeam = awayTeamName,
            FinalScore = scoreText,
            Cards = ExtractGranularCards(document),
            Penalties = ExtractGranularPenalties(document)
        };
    }

    private static string? ExtractRefereeIdFromMatchDetail(IDocument document)
    {
        var refereeLink = document.QuerySelector("a[href*='/schiedsrichter/']");
        var href = refereeLink?.GetAttribute("href") ?? string.Empty;
        return ExtractIdFromHref(href);
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

    private static List<CardEvent> ExtractGranularCards(IDocument document)
    {
        var cards = new List<CardEvent>();
        var items = document.QuerySelectorAll("#sb-karten ul li");
        
        foreach (var item in items)
        {
            var isHome = item.ClassList.Contains("sb-aktion-heim");
            var uhrElement = item.QuerySelector("div.sb-aktion-uhr span.sb-sprite-uhr-klein");
            if (uhrElement == null) continue;
            
            var minute = ParseMinuteFromSprite(uhrElement);
            
            var spriteEl = item.QuerySelector("div.sb-aktion-spielstand span.sb-sprite");
            var cardType = "Unknown";
            if (spriteEl != null)
            {
                if (spriteEl.ClassList.Contains("sb-gelb")) cardType = "Yellow";
                else if (spriteEl.ClassList.Contains("sb-gelbrot")) cardType = "SecondYellow";
                else if (spriteEl.ClassList.Contains("sb-rot")) cardType = "Red";
            }
            
            if (cardType == "Unknown") continue; // Not a card event
            
            var aktionText = item.QuerySelector("div.sb-aktion-aktion")?.TextContent ?? "";
            var parts = aktionText.Split(new[] { ',' }, 2);
            var reason = parts.Length > 1 ? parts[1].Trim() : null;
            
            cards.Add(new CardEvent
            {
                Minute = minute,
                IsHomeTeam = isHome,
                CardType = cardType,
                Reason = reason
            });
        }
        
        return cards;
    }

    private static List<PenaltyEvent> ExtractGranularPenalties(IDocument document)
    {
        var penalties = new List<PenaltyEvent>();
        
        var items = document.QuerySelectorAll("ul li");
        foreach (var item in items)
        {
            var isHome = item.ClassList.Contains("sb-aktion-heim");
            var isAway = item.ClassList.Contains("sb-aktion-gast");
            if (!isHome && !isAway) continue;
            
            var spriteEl = item.QuerySelector("span.sb-sprite.sb-elfmetertor, span.sb-sprite.sb-elfmeter-verschossen");
            var aktionText = item.QuerySelector("div.sb-aktion-aktion")?.TextContent ?? "";
            
            string? type = null;
            if (spriteEl != null)
            {
                if (spriteEl.ClassList.Contains("sb-elfmetertor")) type = "Scored";
                else if (spriteEl.ClassList.Contains("sb-elfmeter-verschossen")) type = "Missed";
            }
            else if (aktionText.Contains("Penalty", StringComparison.OrdinalIgnoreCase) || aktionText.Contains("Elfmeter", StringComparison.OrdinalIgnoreCase))
            {
                if (aktionText.Contains("Missed", StringComparison.OrdinalIgnoreCase) || aktionText.Contains("verschossen", StringComparison.OrdinalIgnoreCase))
                    type = "Missed";
                else
                    type = "Scored";
            }
            
            if (type == null) continue;
            
            var uhrElement = item.QuerySelector("div.sb-aktion-uhr span.sb-sprite-uhr-klein");
            if (uhrElement == null) continue;
            var minute = ParseMinuteFromSprite(uhrElement);
            
            
            penalties.Add(new PenaltyEvent
            {
                Minute = minute,
                IsHomeTeam = isHome,
                Type = type,
                Reason = aktionText.Trim()
            });
        }
        
        return penalties;
    }

    private static int ParseMinuteFromSprite(IElement uhrElement)
    {
        var style = uhrElement.GetAttribute("style") ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(style, @"background-position:\s*-(\d+)px\s*-(\d+)px");
        if (!match.Success) return 0;
        
        var x = int.Parse(match.Groups[1].Value) / 36;
        var y = int.Parse(match.Groups[2].Value) / 36;
        
        var minute = (y * 10) + x + 1;
        
        var text = uhrElement.TextContent.Trim();
        if (text.StartsWith("+") && int.TryParse(text.TrimStart('+'), out var extra))
        {
            minute += extra; 
        }
        
        return minute;
    }
}

/// <summary>Intermediate model for match summaries extracted from league page.</summary>
public sealed class MatchSummary
{
    public required string MatchId { get; init; }
    public required string HomeTeam { get; init; }
    public required string AwayTeam { get; init; }
    public string? FinalScore { get; init; }
}
