using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace RefereeStats.WebApi.Services;

public class User
{
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
}

public sealed class AuthService
{
    private readonly string _usersFilePath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IConfiguration configuration, ILogger<AuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        _usersFilePath = Path.Combine(dir, "users.json");

        EnsureAdminExists();
    }

    private void EnsureAdminExists()
    {
        if (File.Exists(_usersFilePath)) return;

        // Create default admin on first run
        var defaultPassword = _configuration["DefaultAdminPassword"] ?? "admin123_ChangeMe!";
        var admin = new User 
        { 
            Email = "admin@refereestats.com", 
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword) 
        };

        File.WriteAllText(_usersFilePath, JsonSerializer.Serialize(new[] { admin }, new JsonSerializerOptions { WriteIndented = true }));
        _logger.LogInformation("Default admin user created at {Path}", _usersFilePath);
    }

    private List<User> LoadUsers()
    {
        if (!File.Exists(_usersFilePath)) return new List<User>();
        var json = File.ReadAllText(_usersFilePath);
        return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
    }

    public string? Authenticate(string email, string password)
    {
        var users = LoadUsers();
        var user = users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return GenerateJwtToken(user);
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSecret = _configuration["Jwt:Key"] ?? "super_secret_fallback_key_that_should_be_long_enough_in_production";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Email),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "DashboardUser")
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "RefereeStatsAPI",
            audience: _configuration["Jwt:Audience"] ?? "RefereeStatsDashboard",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
