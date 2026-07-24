using Microsoft.Extensions.DependencyInjection;
using RefereeStats.ApifyActor;
using RefereeStats.Core.Export;
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

    var since = input.Since is { Length: > 0 } s ? DateOnly.Parse(s) : (DateOnly?)null;
    var profiles = await scraper.GetRefereeProfilesAsync(input.League, input.Season, since);

    await apifyClient.PushItemsAsync(datasetId, profiles);

    var timestamp = Environment.GetEnvironmentVariable("RUN_TIMESTAMP_OVERRIDE")
        ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
    const string exportDir = "/app/exports";
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

    Log.Information("Run complete: {Count} referee profiles exported and pushed to dataset {DatasetId}",
        profiles.Count, datasetId);
}

internal sealed class ActorInput
{
    public string League { get; init; } = "laliga2";
    public string Season { get; init; } = "2025-2026";
    public string Mode { get; init; } = "profile";
    public string? Since { get; init; }
}
