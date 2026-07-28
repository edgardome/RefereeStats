using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RefereeStats.WebApi.Services;

public class RapidApiAuthOptions : AuthenticationSchemeOptions
{
}

public class RapidApiAuthHandler : AuthenticationHandler<RapidApiAuthOptions>
{
    private const string HeaderName = "X-RapidAPI-Proxy-Secret";
    private readonly IConfiguration _configuration;

    public RapidApiAuthHandler(
        IOptionsMonitor<RapidApiAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedSecret = _configuration["RapidApi:ProxySecret"];

        // If no secret is configured, this scheme doesn't authenticate anything
        if (string.IsNullOrEmpty(expectedSecret))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(HeaderName, out var provided) || provided != expectedSecret)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "RapidApiClient"),
            new Claim(ClaimTypes.Role, "RapidApiClientRole")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
