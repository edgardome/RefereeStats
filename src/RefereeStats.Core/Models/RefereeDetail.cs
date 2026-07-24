namespace RefereeStats.Core.Models;

/// <summary>Ficha del árbitro (página de perfil individual en Transfermarkt): datos biográficos, no agregados por liga/temporada.</summary>
public sealed class RefereeDetail
{
    public required string RefereeId { get; init; }
    public required string Name { get; init; }
    public string? Nationality { get; init; }
    public DateOnly? BirthDate { get; init; }
}
