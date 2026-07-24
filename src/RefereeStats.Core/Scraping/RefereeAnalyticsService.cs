using RefereeStats.Core.Models;

namespace RefereeStats.Core.Scraping;

/// <summary>Derives Level 3 metrics from Level 2 match data.</summary>
public static class RefereeAnalyticsService
{
    public static RefereeAnalytics ComputeAnalytics(string refereeId, IReadOnlyList<MatchCardEvent> matches)
    {
        if (matches.Count == 0)
        {
            return new RefereeAnalytics { RefereeId = refereeId };
        }

        var homeAwayBias = ComputeHomeAwayCardBiasRatio(matches);
        var last10Trend = ComputeLast10MatchesTrend(matches);
        var teamHistory = ComputeRefereeTeamHistory(matches);

        return new RefereeAnalytics
        {
            RefereeId = refereeId,
            HomeAwayCardBiasRatio = homeAwayBias,
            Last10MatchesTrend = last10Trend,
            RefereeTeamHistory = teamHistory,
        };
    }

    /// <summary>Home cards / Away cards ratio. If away = 0, returns 0.</summary>
    private static decimal ComputeHomeAwayCardBiasRatio(IReadOnlyList<MatchCardEvent> matches)
    {
        var homeCards = matches.Sum(m => m.HomeCards);
        var awayCards = matches.Sum(m => m.AwayCards);

        if (awayCards == 0)
        {
            return 0m;
        }

        var ratio = (decimal)homeCards / awayCards;
        return Math.Round(ratio, 2);
    }

    /// <summary>Percent change in cards per match: recent (last 10 or all) vs season average.</summary>
    private static decimal ComputeLast10MatchesTrend(IReadOnlyList<MatchCardEvent> matches)
    {
        var seasonAvg = matches.Count == 0 ? 0 : (decimal)matches.Sum(m => m.HomeCards + m.AwayCards) / matches.Count;
        if (seasonAvg == 0)
        {
            return 0m;
        }

        var last10 = matches.TakeLast(10).ToList();
        var last10Avg = last10.Count == 0 ? 0 : (decimal)last10.Sum(m => m.HomeCards + m.AwayCards) / last10.Count;

        var trend = ((last10Avg - seasonAvg) / seasonAvg) * 100; // percentage points
        return Math.Round(trend, 2);
    }

    /// <summary>Cards per team opponent: group by team, count matches, calculate avg cards/match.</summary>
    private static List<RefereeTeamHistory> ComputeRefereeTeamHistory(IReadOnlyList<MatchCardEvent> matches)
    {
        var teamStats = new Dictionary<string, (int count, int cards)>();

        foreach (var match in matches)
        {
            // Record cards given against away team
            if (!teamStats.ContainsKey(match.AwayTeam))
            {
                teamStats[match.AwayTeam] = (0, 0);
            }

            var (count, cards) = teamStats[match.AwayTeam];
            teamStats[match.AwayTeam] = (count + 1, cards + match.AwayCards);

            // Record cards given against home team (when referee is in away position conceptually)
            // For consistency, also track home team interactions
            if (!teamStats.ContainsKey(match.HomeTeam))
            {
                teamStats[match.HomeTeam] = (0, 0);
            }

            (count, cards) = teamStats[match.HomeTeam];
            teamStats[match.HomeTeam] = (count + 1, cards + match.HomeCards);
        }

        var history = teamStats
            .Select(kvp => new RefereeTeamHistory
            {
                TeamId = kvp.Key,
                MatchesTogether = kvp.Value.count,
                AvgCardsAgainstTeam = kvp.Value.count == 0 ? 0m : Math.Round((decimal)kvp.Value.cards / kvp.Value.count, 2),
            })
            .OrderByDescending(h => h.MatchesTogether)
            .ToList();

        return history;
    }
}
