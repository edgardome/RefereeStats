using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Serilog;

namespace RefereeStats.Core.Scraping;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra TransfermarktClient/RefereeScraper con HttpClientFactory, User-Agent
    /// de navegador real, y reintentos con backoff exponencial (Polly).
    /// </summary>
    public static IServiceCollection AddRefereeScraping(this IServiceCollection services)
    {
        services.AddSingleton(Log.Logger);

        services.AddHttpClient<TransfermarktClient>(c =>
        {
            c.BaseAddress = new Uri("https://www.transfermarkt.com");
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            c.Timeout = TimeSpan.FromSeconds(30);
        }).AddPolicyHandler(GetRetryPolicy());

        services.AddTransient<RefereeScraper>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        => HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}
