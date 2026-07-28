using AspNetCoreRateLimit;
using RefereeStats.Core.Scraping;
using Serilog;
using RefereeStats.WebApi.Services;
using RefereeStats.WebApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddControllers(options => 
{
    options.Filters.Add<ScrapeExceptionFilter>();
    options.Filters.Add<RefereeStats.WebApi.Filters.FreemiumLeagueFilterAttribute>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Referee Stats API",
        Version = "v1",
        Description = "Estadísticas de árbitros de fútbol (Transfermarkt) — publicado vía RapidAPI.",
    });
});

builder.Services.AddSingleton<AuthService>();
builder.Services.AddRefereeScraping();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "RefereeStatsAPI",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "RefereeStatsDashboard",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "super_secret_fallback_key_that_should_be_long_enough_in_production"))
    };
})
.AddScheme<RapidApiAuthOptions, RapidApiAuthHandler>("RapidApiProxySecret", null);

builder.Services.AddAuthorization(options =>
{
    // Require EITHER a valid JWT or a valid RapidAPI Secret
    var defaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme, "RapidApiProxySecret")
        .RequireAuthenticatedUser()
        .Build();
    
    options.DefaultPolicy = defaultPolicy;
    // FallbackPolicy is removed so FreemiumLeagueFilter can manage selective access
});
builder.Services.AddHostedService<MatchSyncBackgroundService>();

// Rate limiting propio como red de seguridad adicional, independiente del de RapidAPI.
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseIpRateLimiting();

app.UseAuthentication();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

app.Run();
