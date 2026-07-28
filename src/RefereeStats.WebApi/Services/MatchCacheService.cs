using System.Text.Json;
using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;
using Serilog;

namespace RefereeStats.WebApi.Services;

public static class MatchCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetCacheFilePath(string league, string season)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "cache");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{league}_{season}_matches.json");
    }

    public static string GetRefereesCacheFilePath(string league, string season)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "cache");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{league}_{season}_referees.json");
    }

    public static async Task<(int CountSynced, int TotalCount)> SyncLeagueIncrementalAsync(
        string league, 
        string season, 
        int? limit, 
        RefereeScraper scraper, 
        CancellationToken ct)
    {
        var path = GetCacheFilePath(league, season);
        var existingReports = new List<GranularMatchReport>();

        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                existingReports = JsonSerializer.Deserialize<List<GranularMatchReport>>(json, JsonOptions) ?? new List<GranularMatchReport>();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SyncLeagueIncrementalAsync: Failed to deserialize cache file at {Path}. Starting fresh.", path);
            }
        }

        var existingIds = existingReports.Select(r => r.MatchId).Where(id => id != null).ToHashSet();
        
        Log.Information("SyncLeagueIncrementalAsync: Fetching matchday calendar for {League} ({Season})...", league, season);
        var matchIds = await scraper.GetMatchdayCalendarAsync(league, season, ct);
        
        var missingIds = matchIds.Where(id => !existingIds.Contains(id)).ToList();
        
        if (limit.HasValue)
        {
            missingIds = missingIds.Take(limit.Value).ToList();
        }

        Log.Information("SyncLeagueIncrementalAsync: Found {Total} matches on calendar. {Missing} are new/missing (Limit: {Limit}).", matchIds.Count, missingIds.Count, limit);

        var countSynced = 0;
        if (missingIds.Count > 0)
        {
            Log.Information("SyncLeagueIncrementalAsync: Downloading details progressively for {Count} new matches in chunks...", missingIds.Count);
            
            var chunkSize = 5; // Download 5 matches concurrently
            for (int i = 0; i < missingIds.Count; i += chunkSize)
            {
                if (ct.IsCancellationRequested) break;

                var chunkIds = missingIds.Skip(i).Take(chunkSize).ToList();
                
                var tasks = chunkIds.Select(async matchId =>
                {
                    try
                    {
                        return await scraper.GetSingleGranularMatchReportAsync(matchId, ct);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "SyncLeagueIncrementalAsync: Failed to scrape match report for ID {MatchId}", matchId);
                        return null;
                    }
                });

                var chunkReports = await Task.WhenAll(tasks);
                var validReports = chunkReports.Where(r => r != null).ToList();

                if (validReports.Count > 0)
                {
                    existingReports.AddRange(validReports!);
                    
                    // Save progressively after each chunk
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(existingReports, JsonOptions), ct);
                    countSynced += validReports.Count;
                }
            }
            
            Log.Information("SyncLeagueIncrementalAsync: Successfully saved {Count} new matches progressively. Total cached matches: {TotalCount}", countSynced, existingReports.Count);
        }

        // Si hemos encontrado nuevos partidos, o si no existe la caché de árbitros, forzamos la actualización de la caché de árbitros
        var refCachePath = GetRefereesCacheFilePath(league, season);
        if (countSynced > 0 || !File.Exists(refCachePath))
        {
            Log.Information("SyncLeagueIncrementalAsync: Updating referee profiles cache for {League} ({Season})...", league, season);
            try
            {
                var profiles = await scraper.GetRefereeProfilesAsync(league, season, ct: ct);
                await File.WriteAllTextAsync(refCachePath, JsonSerializer.Serialize(profiles, JsonOptions), ct);
                Log.Information("SyncLeagueIncrementalAsync: Saved {Count} referee profiles.", profiles.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SyncLeagueIncrementalAsync: Failed to scrape referee profiles for {League}", league);
            }
        }

        return (countSynced, existingReports.Count);
    }
    public static async Task<List<GranularMatchReport>> LoadAllMatchesFromCacheAsync(string league, string season, CancellationToken ct = default)
    {
        var path = GetCacheFilePath(league, season);
        if (!File.Exists(path)) return new List<GranularMatchReport>();

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<GranularMatchReport>>(json, JsonOptions) ?? new List<GranularMatchReport>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load matches from cache at {Path}", path);
            return new List<GranularMatchReport>();
        }
    }

    public static async Task<List<GranularMatchReport>> LoadAllMatchesForRefereeAcrossLeaguesAsync(string refereeId, string season, CancellationToken ct = default)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "cache");
        if (!Directory.Exists(dir)) return new List<GranularMatchReport>();

        var files = Directory.GetFiles(dir, $"*_{season}_matches.json");
        var allReports = new List<GranularMatchReport>();

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var reports = JsonSerializer.Deserialize<List<GranularMatchReport>>(json, JsonOptions);
                if (reports != null)
                {
                    allReports.AddRange(reports.Where(r => r.RefereeId == refereeId));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load cross-league matches from cache at {Path}", file);
            }
        }

        return allReports
            .GroupBy(r => r.MatchId)
            .Select(g => g.First())
            .ToList();
    }

    public static async Task<List<RefereeProfile>> LoadRefereesFromCacheAsync(string league, string season, CancellationToken ct = default)
    {
        var path = GetRefereesCacheFilePath(league, season);
        if (!File.Exists(path)) return new List<RefereeProfile>();

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<RefereeProfile>>(json, JsonOptions) ?? new List<RefereeProfile>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load referees from cache at {Path}", path);
            return new List<RefereeProfile>();
        }
    }

    public static async Task SaveRefereesToCacheAsync(string league, string season, IReadOnlyList<RefereeProfile> profiles, CancellationToken ct = default)
    {
        var path = GetRefereesCacheFilePath(league, season);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profiles, JsonOptions), ct);
    }
}
