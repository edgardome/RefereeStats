namespace RefereeStats.Core.Models;

/// <summary>Nivel 3: métricas derivadas/analíticas por árbitro.</summary>
public sealed class RefereeAnalytics
{
    public required string RefereeId { get; init; }
    public decimal HomeAwayCardBiasRatio { get; init; }
    public decimal Last10MatchesTrend { get; init; }
    public List<RefereeTeamHistory> RefereeTeamHistory { get; init; } = [];
}

public sealed class RefereeTeamHistory
{
    public required string TeamId { get; init; }
    public int MatchesTogether { get; init; }
    public decimal AvgCardsAgainstTeam { get; init; }
}
