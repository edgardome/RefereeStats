using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RefereeStats.WebApi.Filters;

public class FreemiumLeagueFilterAttribute : ActionFilterAttribute
{
    private readonly string _freeLeague = "laliga2";

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
            // If it's not a free request, check authentication
            if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Premium access requires authentication. Please log in or provide a valid API Key." });
                return;
            }
        }
        
        base.OnActionExecuting(context);
    }
}
