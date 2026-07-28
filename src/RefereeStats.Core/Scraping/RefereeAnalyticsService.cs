using RefereeStats.Core.Models;

namespace RefereeStats.Core.Scraping;

/// <summary>Derives Level 3 metrics from Level 2 match data.</summary>
public static class RefereeAnalyticsService
{
    public static RefereeAnalytics ComputeAnalytics(string refereeId, IReadOnlyList<GranularMatchReport> matches)
    {
        if (matches.Count == 0)
        {
            return new RefereeAnalytics 
            { 
                RefereeId = refereeId,
                HomeCardsPercentage = 50,
                AwayCardsPercentage = 50,
                HomePenaltiesPercentage = 50,
                AwayPenaltiesPercentage = 50
            };
        }

        var homeAwayBias = ComputeHomeAwayCardBiasRatio(matches);
        var last10Trend = ComputeLast10MatchesTrend(matches);
        var teamHistory = ComputeRefereeTeamHistory(matches);

        // Averages
        var totalYellows = matches.Sum(m => m.Cards.Count(c => c.CardType == "Yellow"));
        var totalDoubleYellows = matches.Sum(m => m.Cards.Count(c => c.CardType == "SecondYellow"));
        var totalReds = matches.Sum(m => m.Cards.Count(c => c.CardType == "Red"));

        var avgYellows = Math.Round((decimal)totalYellows / matches.Count, 2);
        var avgDoubleYellows = Math.Round((decimal)totalDoubleYellows / matches.Count, 2);
        var avgReds = Math.Round((decimal)totalReds / matches.Count, 2);

        // Percentages
        var homeCards = matches.Sum(m => m.Cards.Count(c => c.IsHomeTeam));
        var awayCards = matches.Sum(m => m.Cards.Count(c => !c.IsHomeTeam));
        var totalCards = homeCards + awayCards;

        var homeCardsPct = totalCards == 0 ? 50m : Math.Round(((decimal)homeCards / totalCards) * 100, 2);
        var awayCardsPct = totalCards == 0 ? 50m : Math.Round(((decimal)awayCards / totalCards) * 100, 2);

        // Penalties
        var homePenalties = matches.Sum(m => m.Penalties.Count(p => p.IsHomeTeam));
        var awayPenalties = matches.Sum(m => m.Penalties.Count(p => !p.IsHomeTeam));
        var totalPenalties = homePenalties + awayPenalties;

        var avgPenalties = Math.Round((decimal)totalPenalties / matches.Count, 2);
        var homePenaltiesPct = totalPenalties == 0 ? 50m : Math.Round(((decimal)homePenalties / totalPenalties) * 100, 2);
        var awayPenaltiesPct = totalPenalties == 0 ? 50m : Math.Round(((decimal)awayPenalties / totalPenalties) * 100, 2);

        return new RefereeAnalytics
        {
            RefereeId = refereeId,
            HomeAwayCardBiasRatio = homeAwayBias,
            Last10MatchesTrend = last10Trend,
            
            AverageYellowCards = avgYellows,
            AverageSecondYellowCards = avgDoubleYellows,
            AverageDirectRedCards = avgReds,
            AveragePenaltiesAwarded = avgPenalties,

            HomeCardsPercentage = homeCardsPct,
            AwayCardsPercentage = awayCardsPct,
            HomePenaltiesPercentage = homePenaltiesPct,
            AwayPenaltiesPercentage = awayPenaltiesPct,

            RefereeTeamHistory = teamHistory,
        };
    }

    /// <summary>Home cards / Away cards ratio. If away = 0, returns 0.</summary>
    private static decimal ComputeHomeAwayCardBiasRatio(IReadOnlyList<GranularMatchReport> matches)
    {
        var homeCards = matches.Sum(m => m.Cards.Count(c => c.IsHomeTeam));
        var awayCards = matches.Sum(m => m.Cards.Count(c => !c.IsHomeTeam));

        if (awayCards == 0)
        {
            return 0m;
        }

        var ratio = (decimal)homeCards / awayCards;
        return Math.Round(ratio, 2);
    }

    /// <summary>Percent change in cards per match: recent (last 10 or all) vs season average.</summary>
    private static decimal ComputeLast10MatchesTrend(IReadOnlyList<GranularMatchReport> matches)
    {
        var seasonAvg = matches.Count == 0 ? 0 : (decimal)matches.Sum(m => m.Cards.Count) / matches.Count;
        if (seasonAvg == 0)
        {
            return 0m;
        }

        var last10 = matches.TakeLast(10).ToList();
        var last10Avg = last10.Count == 0 ? 0 : (decimal)last10.Sum(m => m.Cards.Count) / last10.Count;

        var trend = ((last10Avg - seasonAvg) / seasonAvg) * 100; // percentage points
        return Math.Round(trend, 2);
    }

    private class TeamAccumulator
    {
        public int Matches { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }
        public int PenaltiesFor { get; set; }
        public int PenaltiesAgainst { get; set; }
    }

    /// <summary>Cards per team opponent: group by team, count matches, calculate avg cards/match.</summary>
    private static List<RefereeTeamHistory> ComputeRefereeTeamHistory(IReadOnlyList<GranularMatchReport> matches)
    {
        var teamStats = new Dictionary<string, TeamAccumulator>();

        foreach (var match in matches)
        {
            if (!teamStats.ContainsKey(match.HomeTeam)) teamStats[match.HomeTeam] = new TeamAccumulator();
            if (!teamStats.ContainsKey(match.AwayTeam)) teamStats[match.AwayTeam] = new TeamAccumulator();

            var homeAcc = teamStats[match.HomeTeam];
            var awayAcc = teamStats[match.AwayTeam];

            homeAcc.Matches++;
            awayAcc.Matches++;

            // Cards
            homeAcc.YellowCards += match.Cards.Count(c => c.IsHomeTeam && c.CardType == "Yellow");
            homeAcc.RedCards += match.Cards.Count(c => c.IsHomeTeam && (c.CardType == "Red" || c.CardType == "SecondYellow"));
            awayAcc.YellowCards += match.Cards.Count(c => !c.IsHomeTeam && c.CardType == "Yellow");
            awayAcc.RedCards += match.Cards.Count(c => !c.IsHomeTeam && (c.CardType == "Red" || c.CardType == "SecondYellow"));

            // Penalties
            homeAcc.PenaltiesFor += match.Penalties.Count(p => p.IsHomeTeam);
            homeAcc.PenaltiesAgainst += match.Penalties.Count(p => !p.IsHomeTeam);
            awayAcc.PenaltiesFor += match.Penalties.Count(p => !p.IsHomeTeam);
            awayAcc.PenaltiesAgainst += match.Penalties.Count(p => p.IsHomeTeam);

            // Parse score (e.g. "2:1" or "2:1 AET" or "5:4 on pens")
            if (!string.IsNullOrWhiteSpace(match.FinalScore))
            {
                var scoreStr = match.FinalScore.Split(' ')[0]; // Take "2:1" from "2:1 AET"
                var parts = scoreStr.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var homeGoals) && int.TryParse(parts[1], out var awayGoals))
                {
                    homeAcc.GoalsFor += homeGoals;
                    homeAcc.GoalsAgainst += awayGoals;
                    awayAcc.GoalsFor += awayGoals;
                    awayAcc.GoalsAgainst += homeGoals;

                    if (homeGoals > awayGoals)
                    {
                        homeAcc.Wins++;
                        awayAcc.Losses++;
                    }
                    else if (homeGoals < awayGoals)
                    {
                        homeAcc.Losses++;
                        awayAcc.Wins++;
                    }
                    else
                    {
                        homeAcc.Draws++;
                        awayAcc.Draws++;
                    }
                }
            }
        }

        var history = teamStats
            .Select(kvp => new RefereeTeamHistory
            {
                TeamId = kvp.Key,
                MatchesTogether = kvp.Value.Matches,
                Wins = kvp.Value.Wins,
                Draws = kvp.Value.Draws,
                Losses = kvp.Value.Losses,
                GoalsFor = kvp.Value.GoalsFor,
                GoalsAgainst = kvp.Value.GoalsAgainst,
                YellowCardsAgainst = kvp.Value.YellowCards,
                RedCardsAgainst = kvp.Value.RedCards,
                PenaltiesFor = kvp.Value.PenaltiesFor,
                PenaltiesAgainst = kvp.Value.PenaltiesAgainst,
                AvgCardsAgainstTeam = kvp.Value.Matches == 0 ? 0m : Math.Round((decimal)(kvp.Value.YellowCards + kvp.Value.RedCards) / kvp.Value.Matches, 2),
            })
            .OrderByDescending(h => h.MatchesTogether)
            .ToList();

        return history;
    }
}
