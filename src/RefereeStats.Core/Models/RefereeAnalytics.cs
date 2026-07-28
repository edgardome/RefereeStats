namespace RefereeStats.Core.Models;

/// <summary>Nivel 3: métricas derivadas/analíticas por árbitro.</summary>
public sealed class RefereeAnalytics
{
    public required string RefereeId { get; init; }
    public decimal HomeAwayCardBiasRatio { get; init; }
    public decimal Last10MatchesTrend { get; init; }
    
    // Fields required by the dashboard UI
    public decimal AverageYellowCards { get; init; }
    public decimal AverageSecondYellowCards { get; init; }
    public decimal AverageDirectRedCards { get; init; }
    public decimal AveragePenaltiesAwarded { get; init; }
    
    public decimal HomeCardsPercentage { get; init; }
    public decimal AwayCardsPercentage { get; init; }
    public decimal HomePenaltiesPercentage { get; init; }
    public decimal AwayPenaltiesPercentage { get; init; }
    
    public List<RefereeTeamHistory> RefereeTeamHistory { get; init; } = [];
}

public sealed class RefereeTeamHistory
{
    public required string TeamId { get; init; }
    public int MatchesTogether { get; init; }
    public int Wins { get; init; }
    public int Draws { get; init; }
    public int Losses { get; init; }
    public int GoalsFor { get; init; }
    public int GoalsAgainst { get; init; }
    public int YellowCardsAgainst { get; init; }
    public int RedCardsAgainst { get; init; }
    public int PenaltiesFor { get; init; }
    public int PenaltiesAgainst { get; init; }
    public decimal AvgCardsAgainstTeam { get; init; }
}
