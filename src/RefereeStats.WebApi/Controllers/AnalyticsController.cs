using Microsoft.AspNetCore.Mvc;
using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;
using RefereeStats.WebApi.Services;

namespace RefereeStats.WebApi.Controllers;

[ApiController]
[Route("api/v1/analytics")]
[Route("api/analytics")] // backward-compatible
public sealed class AnalyticsController() : ControllerBase
{
    [HttpGet("leagues/{league}/leaderboard")]
    [ProducesResponseType<PagedResponse<RefereeLeaderboardEntry>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<RefereeLeaderboardEntry>>> GetLeaderboard(
        string league, [FromQuery] string season, [FromQuery] string sortBy = "strictness",
        [FromQuery] int page = 0, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var matches = await MatchCacheService.LoadAllMatchesFromCacheAsync(league, season, ct);
        if (matches.Count == 0) return Ok(PagedResponse<RefereeLeaderboardEntry>.Create([], page, size));

        var matchesByReferee = matches.Where(m => !string.IsNullOrEmpty(m.RefereeId)).GroupBy(m => m.RefereeId!).ToList();
        var leaderboard = new List<RefereeLeaderboardEntry>();

        foreach (var group in matchesByReferee)
        {
            var refId = group.Key;
            var refMatches = group.ToList();
            if (refMatches.Count < 3) continue; // Skip referees with too few matches

            var analytics = RefereeAnalyticsService.ComputeAnalytics(refId, refMatches);

            leaderboard.Add(new RefereeLeaderboardEntry
            {
                RefereeId = refId,
                MatchesOfficiated = refMatches.Count,
                AverageYellowCards = analytics.AverageYellowCards,
                AverageRedCards = analytics.AverageDirectRedCards,
                HomeBiasPercentage = analytics.HomeCardsPercentage,
                HomeWinRatio = analytics.HomeWinRatio,
                AwayWinRatio = analytics.AwayWinRatio,
                DrawRatio = analytics.DrawRatio,
            });
        }

        if (sortBy == "homeBias")
        {
            leaderboard = leaderboard.OrderByDescending(x => x.HomeBiasPercentage).ToList();
        }
        else // default to strictness
        {
            leaderboard = leaderboard.OrderByDescending(x => x.AverageYellowCards + (x.AverageRedCards * 2)).ToList();
        }

        for (int i = 0; i < leaderboard.Count; i++)
        {
            leaderboard[i].Rank = i + 1;
        }

        return Ok(PagedResponse<RefereeLeaderboardEntry>.Create(leaderboard, page, size));
    }

    [HttpGet("matchup-preview")]
    [ProducesResponseType<MatchupPreview>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchupPreview>> GetMatchupPreview(
        [FromQuery] string refereeId, [FromQuery] string homeTeam, [FromQuery] string awayTeam, [FromQuery] string league, [FromQuery] string season, [FromQuery] bool global = false, CancellationToken ct = default)
    {
        var refMatches = global 
            ? await MatchCacheService.LoadAllMatchesForRefereeAcrossLeaguesAsync(refereeId, season, ct)
            : (await MatchCacheService.LoadAllMatchesFromCacheAsync(league, season, ct)).Where(m => m.RefereeId == refereeId).ToList();

        if (refMatches.Count == 0) return NotFound("No matches found for this referee in the cache.");

        var analytics = RefereeAnalyticsService.ComputeAnalytics(refereeId, refMatches);
        var teamHistory = analytics.RefereeTeamHistory;

        var homeStats = teamHistory.FirstOrDefault(t => string.Equals(t.TeamId, homeTeam, StringComparison.OrdinalIgnoreCase));
        var awayStats = teamHistory.FirstOrDefault(t => string.Equals(t.TeamId, awayTeam, StringComparison.OrdinalIgnoreCase));

        var homeAvg = homeStats?.AvgCardsAgainstTeam ?? analytics.AverageYellowCards;
        var awayAvg = awayStats?.AvgCardsAgainstTeam ?? analytics.AverageYellowCards;

        string prediction;
        if (homeAvg > awayAvg + 1) prediction = $"Favors {awayTeam} (Strict against {homeTeam})";
        else if (awayAvg > homeAvg + 1) prediction = $"Favors {homeTeam} (Strict against {awayTeam})";
        else prediction = "Balanced Matchup";

        return Ok(new MatchupPreview
        {
            RefereeId = refereeId,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            HomeTeamMatchesWithReferee = homeStats?.MatchesTogether ?? 0,
            AwayTeamMatchesWithReferee = awayStats?.MatchesTogether ?? 0,
            AvgCardsAgainstHomeTeam = homeAvg,
            AvgCardsAgainstAwayTeam = awayAvg,
            AdvantagePrediction = prediction
        });
    }

    [HttpGet("compare")]
    [ProducesResponseType<RefereeComparison>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RefereeComparison>> CompareReferees(
        [FromQuery] string refereeAId, [FromQuery] string refereeBId, [FromQuery] string league, [FromQuery] string season, [FromQuery] bool global = false, CancellationToken ct = default)
    {
        var matchesA = global 
            ? await MatchCacheService.LoadAllMatchesForRefereeAcrossLeaguesAsync(refereeAId, season, ct)
            : (await MatchCacheService.LoadAllMatchesFromCacheAsync(league, season, ct)).Where(m => m.RefereeId == refereeAId).ToList();
            
        var matchesB = global 
            ? await MatchCacheService.LoadAllMatchesForRefereeAcrossLeaguesAsync(refereeBId, season, ct)
            : (await MatchCacheService.LoadAllMatchesFromCacheAsync(league, season, ct)).Where(m => m.RefereeId == refereeBId).ToList();

        var analyticsA = RefereeAnalyticsService.ComputeAnalytics(refereeAId, matchesA);
        var analyticsB = RefereeAnalyticsService.ComputeAnalytics(refereeBId, matchesB);

        var highlights = new List<string>();
        
        if (analyticsA.AverageYellowCards > analyticsB.AverageYellowCards + 0.5m)
            highlights.Add($"Referee A is significantly stricter with yellow cards ({analyticsA.AverageYellowCards} vs {analyticsB.AverageYellowCards})");
        else if (analyticsB.AverageYellowCards > analyticsA.AverageYellowCards + 0.5m)
            highlights.Add($"Referee B is significantly stricter with yellow cards ({analyticsB.AverageYellowCards} vs {analyticsA.AverageYellowCards})");

        if (analyticsA.HomeCardsPercentage > analyticsB.HomeCardsPercentage + 5)
            highlights.Add("Referee A gives proportionally more cards to home teams than Referee B.");
        else if (analyticsB.HomeCardsPercentage > analyticsA.HomeCardsPercentage + 5)
            highlights.Add("Referee B gives proportionally more cards to home teams than Referee A.");

        if (analyticsA.HomeWinRatio > analyticsB.HomeWinRatio + 0.1m)
            highlights.Add($"Home teams win more often with Referee A ({analyticsA.HomeWinRatio:P0} vs {analyticsB.HomeWinRatio:P0}).");
        else if (analyticsB.HomeWinRatio > analyticsA.HomeWinRatio + 0.1m)
            highlights.Add($"Home teams win more often with Referee B ({analyticsB.HomeWinRatio:P0} vs {analyticsA.HomeWinRatio:P0}).");

        if (highlights.Count == 0) highlights.Add("Both referees have very similar profiles.");

        return Ok(new RefereeComparison
        {
            RefereeA = analyticsA,
            RefereeB = analyticsB,
            DifferenceHighlights = highlights
        });
    }
}
