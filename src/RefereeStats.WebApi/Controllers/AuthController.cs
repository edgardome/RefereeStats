using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RefereeStats.WebApi.Services;

namespace RefereeStats.WebApi.Controllers;

public class LoginRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
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
}
