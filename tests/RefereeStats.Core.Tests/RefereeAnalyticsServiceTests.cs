using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;

namespace RefereeStats.Core.Tests;

public class RefereeAnalyticsServiceTests
{
    private static List<MatchCardEvent> SampleMatches() =>
    [
        new() { MatchId = "1", Date = new DateOnly(2025, 1, 1), HomeTeam = "Barcelona", AwayTeam = "Real Madrid", RefereeId = "123", HomeCards = 3, AwayCards = 1 },
        new() { MatchId = "2", Date = new DateOnly(2025, 1, 8), HomeTeam = "Atletico", AwayTeam = "Valencia", RefereeId = "123", HomeCards = 2, AwayCards = 2 },
        new() { MatchId = "3", Date = new DateOnly(2025, 1, 15), HomeTeam = "Barcelona", AwayTeam = "Sevilla", RefereeId = "123", HomeCards = 1, AwayCards = 4 },
        new() { MatchId = "4", Date = new DateOnly(2025, 1, 22), HomeTeam = "Real Madrid", AwayTeam = "Barcelona", RefereeId = "123", HomeCards = 4, AwayCards = 2 },
    ];

    [Fact]
    public void ComputeAnalytics_WithEmptyMatches_ReturnsZeroMetrics()
    {
        var analytics = RefereeAnalyticsService.ComputeAnalytics("999", []);

        Assert.Equal("999", analytics.RefereeId);
        Assert.Equal(0m, analytics.HomeAwayCardBiasRatio);
        Assert.Equal(0m, analytics.Last10MatchesTrend);
        Assert.Empty(analytics.RefereeTeamHistory);
    }

    [Fact]
    public void ComputeAnalytics_HomeAwayCardBiasRatio()
    {
        // 4 matches: (3+2+1+4) home = 10, (1+2+4+2) away = 9
        // Ratio: 10/9 ≈ 1.11
        var analytics = RefereeAnalyticsService.ComputeAnalytics("123", SampleMatches());

        Assert.Equal(1.11m, analytics.HomeAwayCardBiasRatio);
    }

    [Fact]
    public void ComputeAnalytics_HomeAwayCardBiasRatio_ZeroAwayCards()
    {
        var matches = new List<MatchCardEvent>
        {
            new() { MatchId = "1", Date = new DateOnly(2025, 1, 1), HomeTeam = "A", AwayTeam = "B", RefereeId = "123", HomeCards = 5, AwayCards = 0 },
        };

        var analytics = RefereeAnalyticsService.ComputeAnalytics("123", matches);

        Assert.Equal(0m, analytics.HomeAwayCardBiasRatio);
    }

    [Fact]
    public void ComputeAnalytics_Last10MatchesTrend_WithFewerThan10Matches()
    {
        // With only 4 matches, last10 = all 4 matches = season avg
        // So trend should be 0
        var analytics = RefereeAnalyticsService.ComputeAnalytics("123", SampleMatches());

        Assert.Equal(0m, analytics.Last10MatchesTrend);
    }

    [Fact]
    public void ComputeAnalytics_Last10MatchesTrend_WithMoreThan10Matches()
    {
        // Create 15 matches: first 5 with 1 card each (avg 1), last 10 with 2 cards each (avg 2)
        var matches = Enumerable.Range(0, 15)
            .Select(i => new MatchCardEvent
            {
                MatchId = $"{i}",
                Date = new DateOnly(2025, 1, 1).AddDays(i),
                HomeTeam = $"H{i}",
                AwayTeam = $"A{i}",
                RefereeId = "123",
                HomeCards = i < 5 ? 1 : 2,
                AwayCards = 0,
            })
            .ToList();

        var analytics = RefereeAnalyticsService.ComputeAnalytics("123", matches);

        // Season avg: 5*1 + 10*2 = 25 / 15 = 1.666...
        // Last 10 avg: 10*2 / 10 = 2
        // Trend: (2 - 1.666...) / 1.666... * 100 = 20.0
        Assert.Equal(20.00m, analytics.Last10MatchesTrend);
    }

    [Fact]
    public void ComputeAnalytics_RefereeTeamHistory_GroupsByTeam()
    {
        var analytics = RefereeAnalyticsService.ComputeAnalytics("123", SampleMatches());

        // Barcelona: matches 1 (3 home), 3 (1 home), 4 (2 away) = 3 matches, 3+1+2 = 6 cards, avg 2.0
        var barcelona = analytics.RefereeTeamHistory.FirstOrDefault(h => h.TeamId == "Barcelona");
        Assert.NotNull(barcelona);
        Assert.Equal(3, barcelona!.MatchesTogether);
        Assert.Equal(2.00m, barcelona!.AvgCardsAgainstTeam);
    }

    [Fact]
    public void ComputeAnalytics_RefereeTeamHistory_SortedByMatchCount()
    {
        var analytics = RefereeAnalyticsService.ComputeAnalytics("123", SampleMatches());

        // Barcelona: 2 matches, Real Madrid: 2 matches, Atletico: 1, Valencia: 1, Sevilla: 1
        var first = analytics.RefereeTeamHistory.First();
        Assert.True(first.MatchesTogether >= 2);
    }
}
