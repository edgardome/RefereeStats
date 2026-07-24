namespace RefereeStats.WebApi;

/// <summary>
/// Valida que la petición venga del proxy de RapidAPI comprobando la cabecera
/// X-RapidAPI-Proxy-Secret contra el secreto configurado. Rechaza cualquier
/// petición directa que no pase por RapidAPI.
/// </summary>
public sealed class RapidApiProxySecretMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const string HeaderName = "X-RapidAPI-Proxy-Secret";

    public async Task InvokeAsync(HttpContext context)
    {
        var expectedSecret = configuration["RapidApi:ProxySecret"];

        if (string.IsNullOrEmpty(expectedSecret))
        {
            // Sin secreto configurado (p.ej. entorno local): no se aplica la validación.
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided) || provided != expectedSecret)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: request did not originate from RapidAPI proxy.");
            return;
        }

        await next(context);
    }
}
