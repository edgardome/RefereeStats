using System.Net.Http.Json;
using System.Text.Json;

namespace RefereeStats.ApifyActor;

/// <summary>
/// Wrapper fino sobre la Apify API REST (sin SDK oficial de .NET).
/// Docs: https://docs.apify.com/api/v2 — input/dataset/key-value store.
/// </summary>
public sealed class ApifyClient(HttpClient httpClient, string token)
{
    public async Task<T?> GetInputAsync<T>(string storeId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(
            $"v2/key-value-stores/{storeId}/records/INPUT?token={token}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    public async Task PushItemsAsync<T>(string datasetId, IEnumerable<T> items, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"v2/datasets/{datasetId}/items?token={token}", items, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SaveMetadataAsync(string storeId, string key, object value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PutAsync(
            $"v2/key-value-stores/{storeId}/records/{key}?token={token}", content, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Pay-Per-Event: configurar precios en Apify Console → Actor → Monetization
    /// Eventos: "actor-start" (1x por run), "profile-item", "match-detail-item", "analytics-item"
    /// </summary>
    public async Task ChargeEventAsync(string eventName, int count = 1, CancellationToken ct = default)
    {
        var runId = Environment.GetEnvironmentVariable("APIFY_ACTOR_RUN_ID");
        if (string.IsNullOrEmpty(runId)) return; // Not running inside Apify platform

        var payload = new { eventName = eventName, eventCount = count };
        try
        {
            var response = await httpClient.PostAsJsonAsync($"v2/actor-runs/{runId}/charge?token={token}", payload, ct);
            response.EnsureSuccessStatusCode();
        }
        catch 
        {
            // Ignore charge errors so we don't crash extraction
        }
    }
}
