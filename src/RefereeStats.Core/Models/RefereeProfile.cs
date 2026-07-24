namespace RefereeStats.Core.Models;

/// <summary>Nivel 1: perfil de árbitro agregado por temporada.</summary>
public sealed class RefereeProfile
{
    public required string RefereeId { get; init; }
    public required string Name { get; init; }
    public string? Nationality { get; init; }
    public DateOnly? BirthDate { get; init; }
    public required string League { get; init; }
    public required string Season { get; init; }
    public int MatchesOfficiated { get; init; }
    public int YellowCards { get; init; }
    public int SecondYellowCards { get; init; }
    public int DirectRedCards { get; init; }
    public int PenaltiesAwarded { get; init; }

    public decimal CardsPerMatch => MatchesOfficiated == 0
        ? 0m
        : Math.Round((decimal)(YellowCards + SecondYellowCards + DirectRedCards) / MatchesOfficiated, 2);
}
