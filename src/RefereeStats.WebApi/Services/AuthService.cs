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
    public string? StripeCustomerId { get; set; }
    public bool IsPremium { get; set; }
}

public sealed class AuthService
{
    private readonly string _usersFilePath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly object _lock = new object();

    public AuthService(IConfiguration configuration, ILogger<AuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var dataDirEnv = Environment.GetEnvironmentVariable("DATA_DIR");
        var dir = !string.IsNullOrEmpty(dataDirEnv) 
            ? dataDirEnv 
            : Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        _usersFilePath = Path.Combine(dir, "users.json");

        EnsureAdminExists();
    }

    private void EnsureAdminExists()
    {
        lock (_lock)
        {
            if (File.Exists(_usersFilePath)) return;

            // Create default admin on first run
            var defaultPassword = _configuration["DefaultAdminPassword"] ?? "admin123_ChangeMe!";
            var admin = new User 
            { 
                Email = "admin@refereestats.com", 
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword),
                IsPremium = true 
            };

            File.WriteAllText(_usersFilePath, JsonSerializer.Serialize(new[] { admin }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.LogInformation("Default admin user created at {Path}", _usersFilePath);
        }
    }

    public List<User> LoadUsers()
    {
        lock (_lock)
        {
            if (!File.Exists(_usersFilePath)) return new List<User>();
            var json = File.ReadAllText(_usersFilePath);
            return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
        }
    }

    public void SaveUsers(List<User> users)
    {
        lock (_lock)
        {
            File.WriteAllText(_usersFilePath, JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public string? RegisterUser(string email, string password)
    {
        var users = LoadUsers();
        if (users.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
        {
            return null; // User already exists
        }

        var newUser = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsPremium = false
        };

        users.Add(newUser);
        SaveUsers(users);

        return GenerateJwtToken(newUser);
    }

    public void UpgradeUser(string email, string stripeCustomerId)
    {
        var users = LoadUsers();
        var user = users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        if (user != null)
        {
            user.IsPremium = true;
            user.StripeCustomerId = stripeCustomerId;
            SaveUsers(users);
        }
    }

    public void DowngradeUserByStripeId(string stripeCustomerId)
    {
        var users = LoadUsers();
        var user = users.FirstOrDefault(u => u.StripeCustomerId == stripeCustomerId);
        if (user != null)
        {
            user.IsPremium = false;
            user.StripeCustomerId = null;
            SaveUsers(users);
        }
    }

    public bool UpdateEmail(string oldEmail, string newEmail)
    {
        var users = LoadUsers();
        if (users.Any(u => u.Email.Equals(newEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return false; // Email already in use
        }

        var user = users.FirstOrDefault(u => u.Email.Equals(oldEmail, StringComparison.OrdinalIgnoreCase));
        if (user != null)
        {
            user.Email = newEmail;
            SaveUsers(users);
            return true;
        }
        return false;
    }

    public bool UpdatePassword(string email, string newPassword)
    {
        var users = LoadUsers();
        var user = users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        if (user != null)
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            SaveUsers(users);
            return true;
        }
        return false;
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
    
    public string? RefreshToken(string email)
    {
        var user = GetUserByEmail(email);
        if (user == null) return null;
        return GenerateJwtToken(user);
    }

    public User? GetUserByEmail(string email)
    {
        var users = LoadUsers();
        return users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSecret = _configuration["Jwt:Key"] ?? "super_secret_fallback_key_that_should_be_long_enough_in_production";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Email),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("is_premium", user.IsPremium.ToString().ToLower()),
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
