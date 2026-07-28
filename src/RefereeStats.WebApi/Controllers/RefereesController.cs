using Microsoft.AspNetCore.Mvc;
using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;
using Microsoft.AspNetCore.Authorization;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RefereeStats.WebApi.Services;

namespace RefereeStats.WebApi.Controllers;

[ApiController]
[Route("api/v1")]
[Route("api")] // backward-compatible: old /api routes still work
public sealed class RefereesController(RefereeScraper scraper, IConfiguration configuration, IMemoryCache cache) : ControllerBase
{
    /// <summary>Nivel 1: perfil agregado por temporada de un árbitro.</summary>
    [HttpGet("referees/{refereeId}/profile")]
    [ProducesResponseType<RefereeProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RefereeProfile>> GetProfile(
        string refereeId, [FromQuery] string league, [FromQuery] string season, CancellationToken ct)
    {
        var profiles = await scraper.GetRefereeProfilesAsync(league, season, ct: ct);
        var profile = profiles.FirstOrDefault(p => p.RefereeId == refereeId);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>Nivel 1b: ficha biográfica del árbitro (nacionalidad, fecha de nacimiento).</summary>
    [HttpGet("referees/{refereeId}/details")]
    [ProducesResponseType<RefereeDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RefereeDetail>> GetDetails(string refereeId, CancellationToken ct)
    {
        var detail = await scraper.GetRefereeDetailAsync(refereeId, ct);
        return Ok(detail);
    }

    /// <summary>Nivel 1c: desglose por temporada de un árbitro dentro de una competición concreta.</summary>
    [HttpGet("referees/{refereeId}/competitions/{league}/history")]
    [ProducesResponseType<PagedResponse<RefereeProfile>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<RefereeProfile>>> GetCompetitionHistory(
        string refereeId, string league, [FromQuery] int page = 0, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var history = await scraper.GetRefereeCompetitionHistoryAsync(refereeId, league, ct);
        return Ok(PagedResponse<RefereeProfile>.Create(history, page, size));
    }

    /// <summary>Level 2: Matches officiated, optionally filtered by date (incremental). Paginated. Reads from local reverse index.</summary>
    [HttpGet("referees/{refereeId}/matches")]
    [ProducesResponseType<PagedResponse<GranularMatchReport>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<GranularMatchReport>>> GetMatches(
        string refereeId, [FromQuery] string league, [FromQuery] string season,
        [FromQuery] DateOnly? since, [FromQuery] bool global = false,
        [FromQuery] int page = 0, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var matches = await LoadMatchesFromCacheAsync(league, season, refereeId, global, ct);
        if (since.HasValue)
        {
            matches = matches.Where(m => m.Date >= since.Value).ToList();
        }
        return Ok(PagedResponse<GranularMatchReport>.Create(matches, page, size));
    }

    [HttpGet("referees/{refereeId}/analytics")]
    [ProducesResponseType<RefereeAnalytics>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RefereeAnalytics>> GetAnalytics(
        string refereeId, [FromQuery] string league, [FromQuery] string season, [FromQuery] bool global = false, CancellationToken ct = default)
    {
        var matches = await LoadMatchesFromCacheAsync(league, season, refereeId, global, ct);
        var analytics = RefereeAnalyticsService.ComputeAnalytics(refereeId, matches);

        try
        {
            var cacheKey = $"referees_{league}_{season}";
            if (!cache.TryGetValue(cacheKey, out IReadOnlyList<RefereeProfile>? profiles) || profiles == null)
            {
                profiles = await scraper.GetRefereeProfilesAsync(league, season, ct: ct);
                cache.Set(cacheKey, profiles, TimeSpan.FromMinutes(5));
            }

            var profile = profiles.FirstOrDefault(p => p.RefereeId == refereeId);
            if (profile != null)
            {
                analytics = new RefereeAnalytics
                {
                    RefereeId = analytics.RefereeId,
                    HomeAwayCardBiasRatio = analytics.HomeAwayCardBiasRatio,
                    Last10MatchesTrend = analytics.Last10MatchesTrend,
                    
                    AverageYellowCards = analytics.AverageYellowCards,
                    AverageSecondYellowCards = analytics.AverageSecondYellowCards,
                    AverageDirectRedCards = analytics.AverageDirectRedCards,
                    AveragePenaltiesAwarded = profile.AveragePenaltiesAwarded, // Load official penalties statistics!

                    HomeCardsPercentage = analytics.HomeCardsPercentage,
                    AwayCardsPercentage = analytics.AwayCardsPercentage,
                    HomePenaltiesPercentage = analytics.HomePenaltiesPercentage,
                    AwayPenaltiesPercentage = analytics.AwayPenaltiesPercentage,

                    TotalMatchesAnalyzed = analytics.TotalMatchesAnalyzed,
                    HomeWinRatio = analytics.HomeWinRatio,
                    AwayWinRatio = analytics.AwayWinRatio,
                    DrawRatio = analytics.DrawRatio,

                    RefereeTeamHistory = analytics.RefereeTeamHistory
                };
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load referee profile to populate penalties in analytics");
        }

        return Ok(analytics);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    /// <summary>Synchronizes all granular match reports for a given league and season into the local reverse index (incremental).</summary>
    [HttpPost("leagues/{league}/sync-matches")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Authorize]
    public async Task<ActionResult> SyncLeagueMatches(string league, [FromQuery] string season, [FromQuery] int? limit, CancellationToken ct)
    {
        var (countSynced, totalCount) = await MatchCacheService.SyncLeagueIncrementalAsync(league, season, limit, scraper, ct);
        var path = MatchCacheService.GetCacheFilePath(league, season);
        
        return Ok(new { TotalCount = totalCount, SyncedNew = countSynced, File = path });
    }

    private static async Task<IReadOnlyList<GranularMatchReport>> LoadMatchesFromCacheAsync(string league, string season, string refereeId, bool global, CancellationToken ct)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "cache");
        if (!Directory.Exists(dir))
        {
            return Array.Empty<GranularMatchReport>();
        }

        var searchPattern = global ? $"*_{season}_matches.json" : $"{league}_{season}_matches.json";
        var files = Directory.GetFiles(dir, searchPattern);
        
        var allReports = new List<GranularMatchReport>();

        foreach (var file in files)
        {
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(file, ct);
                var reports = System.Text.Json.JsonSerializer.Deserialize<List<GranularMatchReport>>(json, JsonOptions);
                if (reports != null)
                {
                    allReports.AddRange(reports.Where(r => r.RefereeId == refereeId));
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load cross-league matches from cache at {Path}", file);
            }
        }

        return allReports
            .GroupBy(r => r.MatchId)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Listado paginado de árbitros disponibles para una liga/temporada.</summary>
    [HttpGet("leagues/{league}/referees")]
    [ProducesResponseType<PagedResponse<RefereeProfile>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<RefereeProfile>>> GetLeagueReferees(
        string league, [FromQuery] string season, [FromQuery] int page = 0, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var cacheKey = $"referees_{league}_{season}";
        if (!cache.TryGetValue(cacheKey, out IReadOnlyList<RefereeProfile>? profiles) || profiles == null || profiles.Count == 0)
        {
            var persistentProfiles = await MatchCacheService.LoadRefereesFromCacheAsync(league, season, ct);
            if (persistentProfiles != null && persistentProfiles.Count > 0)
            {
                profiles = persistentProfiles;
            }
            else
            {
                profiles = await scraper.GetRefereeProfilesAsync(league, season, ct: ct);
                await MatchCacheService.SaveRefereesToCacheAsync(league, season, profiles, ct);
            }
            cache.Set(cacheKey, profiles, TimeSpan.FromMinutes(60));
        }

        return Ok(PagedResponse<RefereeProfile>.Create(profiles, page, size));
    }

    /// <summary>Synchronizes all supported leagues from configuration (incremental).</summary>
    [HttpPost("leagues/sync-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Authorize]
    public async Task<ActionResult> SyncAllLeagues([FromQuery] int? limitPerLeague, CancellationToken ct)
    {
        var section = configuration.GetSection("SyncSettings");
        var targets = section.GetSection("Targets").Get<List<MatchSyncBackgroundService.SyncTarget>>() 
                      ?? MatchSyncBackgroundService.GetDefaultTargets();

        var results = new List<object>();

        foreach (var target in targets)
        {
            var (countSynced, totalCount) = await MatchCacheService.SyncLeagueIncrementalAsync(
                target.League, target.Season, limitPerLeague, scraper, ct);

            results.Add(new
            {
                League = target.League,
                Season = target.Season,
                TotalCount = totalCount,
                SyncedNew = countSynced
            });
        }

        return Ok(results);
    }
}
