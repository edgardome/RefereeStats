using Microsoft.Extensions.DependencyInjection;
using RefereeStats.ApifyActor;
using RefereeStats.Core.Export;
using RefereeStats.Core.Models;
using RefereeStats.Core.Scraping;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var services = new ServiceCollection();
services.AddRefereeScraping();
services.AddSingleton<ExportService>();
services.AddHttpClient(nameof(ApifyClient), c =>
{
    var apiBase = Environment.GetEnvironmentVariable("APIFY_API_BASE_URL") ?? "https://api.apify.com/";
    c.BaseAddress = new Uri(apiBase.EndsWith('/') ? apiBase : apiBase + "/");
});

await using var provider = services.BuildServiceProvider();

try
{
    await RunAsync(provider);
    return 0;
}
catch (ScrapeException ex)
{
    Log.Error(ex, "Scrape failed with category {Category}", ex.Category);
    return 1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled error");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task RunAsync(ServiceProvider provider)
{
    var token = Environment.GetEnvironmentVariable("APIFY_TOKEN")
        ?? throw new InvalidOperationException("APIFY_TOKEN env var is required.");
    var inputStoreId = Environment.GetEnvironmentVariable("APIFY_DEFAULT_KEY_VALUE_STORE_ID")
        ?? throw new InvalidOperationException("APIFY_DEFAULT_KEY_VALUE_STORE_ID env var is required.");
    var datasetId = Environment.GetEnvironmentVariable("APIFY_DEFAULT_DATASET_ID")
        ?? throw new InvalidOperationException("APIFY_DEFAULT_DATASET_ID env var is required.");

    var httpClient = provider.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(nameof(ApifyClient));
    var apifyClient = new ApifyClient(httpClient, token);

    var input = await apifyClient.GetInputAsync<ActorInput>(inputStoreId)
        ?? throw new InvalidOperationException("Actor INPUT is empty.");

    Log.Information("Starting run: league={League} season={Season} mode={Mode} since={Since}",
        input.League, input.Season, input.Mode, input.Since);

    var scraper = provider.GetRequiredService<RefereeScraper>();
    var exporter = provider.GetRequiredService<ExportService>();

    if (input.Mode == "matches")
    {
        var matchIds = await scraper.GetMatchdayCalendarAsync(input.League, input.Season);
        Log.Information("Found {Count} matches in calendar. Starting granular download...", matchIds.Count);
        
        var reports = await scraper.GetGranularMatchReportsAsync(matchIds);
        
        // Reverse Indexing: group match reports by RefereeId
        var reverseIndex = reports
            .GroupBy(r => r.RefereeId)
            .Select(g => new
            {
                RefereeId = g.Key,
                Matches = g.ToList()
            })
            .ToList();
            
        await apifyClient.ChargeEventAsync("match-detail-item", reverseIndex.Count);
        await apifyClient.PushItemsAsync(datasetId, reverseIndex);
        
        var timestamp = Environment.GetEnvironmentVariable("RUN_TIMESTAMP_OVERRIDE") ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        const string exportDir = "/app/exports";
        Directory.CreateDirectory(exportDir);
        var jsonlPath = $"{exportDir}/referees-matches-{timestamp}.jsonl";
        
        var jsonLines = reverseIndex.Select(r => System.Text.Json.JsonSerializer.Serialize(r));
        await File.WriteAllLinesAsync(jsonlPath, jsonLines);
        
        await apifyClient.SaveMetadataAsync(inputStoreId, "latest_exports", new
        {
            jsonl = Path.GetFileName(jsonlPath),
        });
        
        Log.Information("Run complete: {Count} referees granular match data exported and pushed to dataset {DatasetId}", reverseIndex.Count, datasetId);
    }
    else if (input.Mode == "profile")
    {
        var since = input.Since is { Length: > 0 } s ? DateOnly.Parse(s) : (DateOnly?)null;
        var profiles = await scraper.GetRefereeProfilesAsync(input.League, input.Season, since);

        await apifyClient.ChargeEventAsync("profile-item", profiles.Count);
        await apifyClient.PushItemsAsync(datasetId, profiles);

        var timestamp = Environment.GetEnvironmentVariable("RUN_TIMESTAMP_OVERRIDE") ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        const string exportDir = "/app/exports";
        Directory.CreateDirectory(exportDir);
        
        var csvPath = $"{exportDir}/referees-{timestamp}.csv";
        var parquetPath = $"{exportDir}/referees-{timestamp}.parquet";
        var jsonlPath = $"{exportDir}/referees-{timestamp}.jsonl";

        await exporter.ExportToCsvAsync(profiles, csvPath);
        await exporter.ExportToParquetAsync(profiles, parquetPath);
        await exporter.ExportToJsonLAsync(profiles, jsonlPath);

        await apifyClient.SaveMetadataAsync(inputStoreId, "latest_exports", new
        {
            csv = Path.GetFileName(csvPath),
            parquet = Path.GetFileName(parquetPath),
            jsonl = Path.GetFileName(jsonlPath),
        });

        Log.Information("Run complete: {Count} referee profiles exported and pushed to dataset {DatasetId}", profiles.Count, datasetId);
    }
    else if (input.Mode == "analytics")
    {
        var matchIds = await scraper.GetMatchdayCalendarAsync(input.League, input.Season);
        Log.Information("Analytics Mode: Found {Count} matches in calendar. Extracting data to compute analytics...", matchIds.Count);
        
        var reports = await scraper.GetGranularMatchReportsAsync(matchIds);
        
        var reverseIndex = reports
            .GroupBy(r => r.RefereeId)
            .ToList();

        var analytics = new List<RefereeAnalytics>();
        foreach (var group in reverseIndex)
        {
            var stats = RefereeAnalyticsService.ComputeAnalytics(group.Key, group.ToList());
            analytics.Add(stats);
        }

        await apifyClient.ChargeEventAsync("analytics-item", analytics.Count);
        await apifyClient.PushItemsAsync(datasetId, analytics);

        var timestamp = Environment.GetEnvironmentVariable("RUN_TIMESTAMP_OVERRIDE") ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        const string exportDir = "/app/exports";
        Directory.CreateDirectory(exportDir);
        
        var csvPath = $"{exportDir}/referees-analytics-{timestamp}.csv";
        var parquetPath = $"{exportDir}/referees-analytics-{timestamp}.parquet";
        var jsonlPath = $"{exportDir}/referees-analytics-{timestamp}.jsonl";

        await exporter.ExportToCsvAsync(analytics, csvPath);
        await exporter.ExportToParquetAsync(analytics, parquetPath);
        await exporter.ExportToJsonLAsync(analytics, jsonlPath);

        await apifyClient.SaveMetadataAsync(inputStoreId, "latest_exports", new
        {
            csv = Path.GetFileName(csvPath),
            parquet = Path.GetFileName(parquetPath),
            jsonl = Path.GetFileName(jsonlPath),
        });

        Log.Information("Run complete: {Count} analytics records exported and pushed to dataset {DatasetId}", analytics.Count, datasetId);
    }
}

internal sealed class ActorInput
{
    public string League { get; init; } = "laliga2";
    public string Season { get; init; } = "2025-2026";
    public string Mode { get; init; } = "profile";
    public string? Since { get; init; }
}
