using System.Net;
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

        // Cookies (p.ej. de verificación anti-bot) capturados en una petición deben
        // reutilizarse en las siguientes: un único CookieContainer compartido por
        // todas las peticiones a Transfermarkt.
        services.AddSingleton<CookieContainer>();

        services.AddHttpClient("RobotsTxt", c =>
        {
            c.BaseAddress = new Uri("https://www.transfermarkt.com");
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            c.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new RobotsTxtChecker(factory.CreateClient("RobotsTxt"), sp.GetRequiredService<ILogger>());
        });

        services.AddHttpClient<TransfermarktClient>(c =>
        {
            c.BaseAddress = new Uri("https://www.transfermarkt.com");
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            c.DefaultRequestHeaders.Add("Connection", "keep-alive");
            c.Timeout = TimeSpan.FromSeconds(120);
        })
        .ConfigurePrimaryHttpMessageHandler(sp => new HttpClientHandler
        {
            CookieContainer = sp.GetRequiredService<CookieContainer>(),
            UseCookies = true,
            // El servidor comprime (gzip/br); sin esto ReadAsStringAsync devuelve bytes
            // binarios crudos y el HTML "parseado" queda vacío (falso "silent failure").
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan) // evita rotar el handler y perder el estado de cookies
        .AddPolicyHandler(GetRetryPolicy());

        services.AddTransient<RefereeScraper>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        => HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutException>()
            .Or<System.Threading.Tasks.TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}
