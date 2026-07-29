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
    private static readonly string StateFilePath = Path.Combine(Environment.CurrentDirectory, "App_Data", "synced_historical.txt");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
        if (File.Exists(StateFilePath))
        {
            var lines = File.ReadAllLines(StateFilePath);
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line)) _completedHistoricalTargets.Add(line.Trim());
            }
        }

        var section = configuration.GetSection("SyncSettings");
        var enabled = section.GetValue("Enabled", true);
        if (!enabled)
        {
            Log.Information("MatchSyncBackgroundService is disabled by configuration.");
            return;
        }

        var intervalHours = section.GetValue("IntervalHours", 12);
        
        var targets = new List<SyncTarget>();
        var leaguesJsonPath = Path.Combine(Environment.CurrentDirectory, "data", "transfermarkt_leagues.json");
        if (File.Exists(leaguesJsonPath))
        {
            try
            {
                var json = File.ReadAllText(leaguesJsonPath);
                var element = JsonDocument.Parse(json).RootElement;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.String)
                    {
                        var code = codeProp.GetString();
                        if (!string.IsNullOrEmpty(code))
                        {
                            // Sync current and previous season for each league by default
                            targets.Add(new SyncTarget { League = code, Season = "2024-2025" });
                            targets.Add(new SyncTarget { League = code, Season = "2023-2024" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load dynamic targets from transfermarkt_leagues.json");
            }
        }
        
        if (targets.Count == 0)
        {
            targets = section.GetSection("Targets").Get<List<SyncTarget>>() ?? GetDefaultTargets();
        }

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
                    File.AppendAllLines(StateFilePath, new[] { targetKey });
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
