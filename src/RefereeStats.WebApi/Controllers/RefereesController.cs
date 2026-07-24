using Microsoft.AspNetCore.Mvc;
using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;

namespace RefereeStats.WebApi.Controllers;

[ApiController]
[Route("api")]
public sealed class RefereesController(RefereeScraper scraper) : ControllerBase
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
    [ProducesResponseType<IReadOnlyList<RefereeProfile>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RefereeProfile>>> GetCompetitionHistory(
        string refereeId, string league, CancellationToken ct)
    {
        var history = await scraper.GetRefereeCompetitionHistoryAsync(refereeId, league, ct);
        return Ok(history);
    }

    /// <summary>Level 2: Matches officiated, optionally filtered by date (incremental).</summary>
    [HttpGet("referees/{refereeId}/matches")]
    [ProducesResponseType<IReadOnlyList<MatchCardEvent>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MatchCardEvent>>> GetMatches(
        string refereeId, [FromQuery] string league, [FromQuery] string season, [FromQuery] DateOnly? since, CancellationToken ct)
    {
        var matches = await scraper.GetRefereeMatchesAsync(refereeId, league, season, since, ct);
        return Ok(matches);
    }

    /// <summary>Level 3: Derived analytics (home/away bias, trends, team history).</summary>
    [HttpGet("referees/{refereeId}/analytics")]
    [ProducesResponseType<RefereeAnalytics>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RefereeAnalytics>> GetAnalytics(
        string refereeId, [FromQuery] string league, [FromQuery] string season, CancellationToken ct)
    {
        var matches = await scraper.GetRefereeMatchesAsync(refereeId, league, season, ct: ct);
        var analytics = RefereeAnalyticsService.ComputeAnalytics(refereeId, matches);
        return Ok(analytics);
    }

    /// <summary>Listado de árbitros disponibles para una liga/temporada.</summary>
    [HttpGet("leagues/{league}/referees")]
    [ProducesResponseType<IReadOnlyList<RefereeProfile>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RefereeProfile>>> GetLeagueReferees(
        string league, [FromQuery] string season, CancellationToken ct)
    {
        var profiles = await scraper.GetRefereeProfilesAsync(league, season, ct: ct);
        return Ok(profiles);
    }
}
