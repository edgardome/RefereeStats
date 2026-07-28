namespace RefereeStats.Core.Models;

public class RefereeLeaderboardEntry
{
    public required string RefereeId { get; set; }
    public int Rank { get; set; }
    public decimal AverageYellowCards { get; set; }
    public decimal AverageRedCards { get; set; }
    public decimal HomeBiasPercentage { get; set; }
    public int MatchesOfficiated { get; set; }
}

public class MatchupPreview
{
    public required string RefereeId { get; set; }
    public required string HomeTeam { get; set; }
    public required string AwayTeam { get; set; }
    public decimal AvgCardsAgainstHomeTeam { get; set; }
    public decimal AvgCardsAgainstAwayTeam { get; set; }
    public int HomeTeamMatchesWithReferee { get; set; }
    public int AwayTeamMatchesWithReferee { get; set; }
    public required string AdvantagePrediction { get; set; }
}

public class RefereeComparison
{
    public required RefereeAnalytics RefereeA { get; set; }
    public required RefereeAnalytics RefereeB { get; set; }
    public List<string> DifferenceHighlights { get; set; } = new();
}
