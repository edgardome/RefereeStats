namespace RefereeStats.Core.Models;

public sealed class GranularMatchReport
{
    public required string MatchId { get; init; }
    public required string RefereeId { get; init; }
    public DateOnly Date { get; init; }
    public required string HomeTeam { get; init; }
    public required string AwayTeam { get; init; }
    public string? FinalScore { get; init; }
    
    public List<CardEvent> Cards { get; init; } = new();
    public List<PenaltyEvent> Penalties { get; init; } = new();
}

public sealed class PenaltyEvent
{
    public int Minute { get; init; }
    public bool IsHomeTeam { get; init; }
    public required string Type { get; init; } // "Scored", "Missed"
    public string? Reason { get; init; } // Optional detail
}

public sealed class CardEvent
{
    public int Minute { get; init; }
    public bool IsHomeTeam { get; init; }
    public required string CardType { get; init; } // "Yellow", "SecondYellow", "Red"
    public string? Reason { get; init; } // e.g., "Falta", "Protestar", etc.
}
