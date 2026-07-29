using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RefereeStats.WebApi.Services;

namespace RefereeStats.WebApi.Controllers;

public class LoginRequest
{
    [Required]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string Email { get; set; }
    
    [Required]
    public required string Password { get; set; }
}

public class ChangeEmailRequest
{
    [Required]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public required string NewEmail { get; set; }
}

public class ChangePasswordRequest
{
    [Required]
    public required string NewPassword { get; set; }
}

[ApiController]
[Route("api/v1/auth")]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult Login([FromBody] LoginRequest request)
    {
        var token = authService.Authenticate(request.Email, request.Password);

        if (token == null)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        return Ok(new { token });
    }

    [HttpGet("me")]
    [Authorize] // Requires a valid JWT token
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult GetMe()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value 
                 ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(email)) return Unauthorized();

        var user = authService.GetUserByEmail(email);
        if (user == null) return NotFound("User not found in database.");

        return Ok(new 
        { 
            email = user.Email, 
            isPremiumInDb = user.IsPremium,
            stripeCustomerId = user.StripeCustomerId
        });
    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult Register([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var token = authService.RegisterUser(request.Email, request.Password);

        if (token == null)
        {
            return BadRequest(new { message = "User already exists or invalid data" });
        }

        return Ok(new { token });
    }

    [HttpPost("refresh")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult Refresh()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value 
                 ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        
        if (string.IsNullOrEmpty(email)) return Unauthorized();

        var token = authService.RefreshToken(email);
        if (token == null) return Unauthorized();

        return Ok(new { token });
    }

    [HttpPost("change-email")]
    [Authorize]
    public ActionResult ChangeEmail([FromBody] ChangeEmailRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var currentEmail = User.FindFirst(ClaimTypes.Email)?.Value 
                        ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(currentEmail)) return Unauthorized();

        if (authService.UpdateEmail(currentEmail, request.NewEmail))
        {
            var token = authService.RefreshToken(request.NewEmail);
            return Ok(new { token });
        }
        return BadRequest(new { message = "Email update failed (maybe already in use)" });
    }

    [HttpPost("change-password")]
    [Authorize]
    public ActionResult ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var currentEmail = User.FindFirst(ClaimTypes.Email)?.Value 
                        ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(currentEmail)) return Unauthorized();

        if (authService.UpdatePassword(currentEmail, request.NewPassword))
        {
            return Ok();
        }
        return BadRequest(new { message = "Password update failed" });
    }
}
