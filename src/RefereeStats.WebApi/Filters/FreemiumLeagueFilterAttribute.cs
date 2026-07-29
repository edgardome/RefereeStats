using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RefereeStats.WebApi.Filters;

public class FreemiumLeagueFilterAttribute : ActionFilterAttribute
{
    private readonly string _freeLeague = "ES2";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Check if the action has [AllowAnonymous]
        if (context.ActionDescriptor.EndpointMetadata.Any(em => em.GetType() == typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute)))
        {
            base.OnActionExecuting(context);
            return;
        }

        // Try to get the league from action arguments (route or query)
        bool isFreeRequest = false;

        if (context.ActionArguments.TryGetValue("league", out var leagueObj) && leagueObj is string league)
        {
            if (string.Equals(league, _freeLeague, StringComparison.OrdinalIgnoreCase))
            {
                isFreeRequest = true;
            }
        }

        if (!isFreeRequest)
        {
            // If it's not a free request, check authentication AND premium status
            if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Premium access requires authentication. Please log in or provide a valid API Key." });
                return;
            }

            var email = context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value 
                     ?? context.HttpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(email))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Invalid token claims." });
                return;
            }

            var authService = context.HttpContext.RequestServices.GetService(typeof(RefereeStats.WebApi.Services.AuthService)) as RefereeStats.WebApi.Services.AuthService;
            var user = authService?.GetUserByEmail(email);

            if (user?.IsPremium != true)
            {
                // We use 403 Forbidden to indicate they are authenticated but lack premium rights
                context.Result = new ObjectResult(new { error = "Premium subscription required." }) { StatusCode = 403 };
                return;
            }
        }
        
        base.OnActionExecuting(context);
    }
}
