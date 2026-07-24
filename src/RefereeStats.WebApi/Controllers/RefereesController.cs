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

    /// <summary>Nivel 2: partidos arbitrados, opcionalmente filtrados desde una fecha (incremental).</summary>
    [HttpGet("referees/{refereeId}/matches")]
    [ProducesResponseType<IReadOnlyList<MatchCardEvent>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<MatchCardEvent>>> GetMatches(
        string refereeId, [FromQuery] DateOnly? since, CancellationToken ct)
    {
        // Nivel 2 requiere scraping por partido, pendiente de implementar en RefereeScraper.
        IReadOnlyList<MatchCardEvent> matches = [];
        return Task.FromResult<ActionResult<IReadOnlyList<MatchCardEvent>>>(Ok(matches));
    }

    /// <summary>Nivel 3: métricas derivadas/analíticas de un árbitro.</summary>
    [HttpGet("referees/{refereeId}/analytics")]
    [ProducesResponseType<RefereeAnalytics>(StatusCodes.Status200OK)]
    public Task<ActionResult<RefereeAnalytics>> GetAnalytics(string refereeId, CancellationToken ct)
    {
        // Nivel 3 se deriva de los datos de Nivel 2, pendiente de implementar.
        var analytics = new RefereeAnalytics { RefereeId = refereeId };
        return Task.FromResult<ActionResult<RefereeAnalytics>>(Ok(analytics));
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
