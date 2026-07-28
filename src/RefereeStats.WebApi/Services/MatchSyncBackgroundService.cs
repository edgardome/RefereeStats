using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;
using System.Text.Json;

namespace RefereeStats.WebApi.Services;

public sealed class MatchSyncBackgroundService(
    IServiceProvider serviceProvider,
    IConfiguration configuration) : BackgroundService
{
    private readonly HashSet<string> _completedHistoricalTargets = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = configuration.GetSection("SyncSettings");
        var enabled = section.GetValue("Enabled", true);
        if (!enabled)
        {
            Log.Information("MatchSyncBackgroundService is disabled by configuration.");
            return;
        }

        var intervalHours = section.GetValue("IntervalHours", 12);
        var targets = section.GetSection("Targets").Get<List<SyncTarget>>() ?? GetDefaultTargets();

        Log.Information("MatchSyncBackgroundService started. Interval: {IntervalHours} hours. Targets count: {Count}", intervalHours, targets.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            Log.Information("Starting background synchronization loop for all target leagues...");

            foreach (var target in targets)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await SyncTargetAsync(target, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error syncing league {League} ({Season}) in background", target.League, target.Season);
                }
            }

            Log.Information("Finished background synchronization loop. Sleeping for {IntervalHours} hours.", intervalHours);
            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
        }
    }

    private async Task SyncTargetAsync(SyncTarget target, CancellationToken ct)
    {
        var targetKey = $"{target.League}_{target.Season}";
        if (_completedHistoricalTargets.Contains(targetKey))
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var scraper = scope.ServiceProvider.GetRequiredService<RefereeScraper>();

        var league = target.League;
        var season = target.Season;

        try
        {
            var (countSynced, totalCount) = await MatchCacheService.SyncLeagueIncrementalAsync(league, season, null, scraper, ct);
            if (countSynced == 0)
            {
                Log.Information("Background Sync: League {League} is already fully synchronized (Total: {TotalCount}).", league, totalCount);
                
                if (IsSeasonFinished(season))
                {
                    _completedHistoricalTargets.Add(targetKey);
                    Log.Information("Background Sync: Target {League} ({Season}) is historical and fully synced. Skipping in future loops.", league, season);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Background Sync: Critical error syncing {League} ({Season}).", league, season);
        }
    }

    private static bool IsSeasonFinished(string season)
    {
        var parts = season.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[1], out var endYear))
        {
            // Assume seasons effectively end by July 1st of the end year.
            var seasonEndDate = new DateTime(endYear, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            return DateTime.UtcNow > seasonEndDate;
        }
        return false;
    }

    public static List<SyncTarget> GetDefaultTargets()
    {
        return new List<SyncTarget>
        {
            new() { League = "laliga", Season = "2024-2025" },
            new() { League = "laliga2", Season = "2024-2025" },
            new() { League = "premier", Season = "2024-2025" },
            new() { League = "championship", Season = "2024-2025" }
        };
    }

    public sealed class SyncTarget
    {
        public string League { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
    }
}
