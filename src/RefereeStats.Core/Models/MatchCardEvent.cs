namespace RefereeStats.Core.Models;

/// <summary>Nivel 2: evento de partido individual arbitrado.</summary>
public sealed class MatchCardEvent
{
    public required string MatchId { get; init; }
    public DateOnly Date { get; init; }
    public required string HomeTeam { get; init; }
    public required string AwayTeam { get; init; }
    public required string RefereeId { get; init; }
    public int HomeCards { get; init; }
    public int AwayCards { get; init; }
    public int? FirstCardMinute { get; init; }
    public string? FinalScore { get; init; }
}
