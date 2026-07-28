using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RefereeStats.Core.Scraping;

namespace RefereeStats.WebApi;

/// <summary>Maps ScrapeException to 503 across all controller actions, uniformly.</summary>
public sealed class ScrapeExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is ScrapeException scrapeEx)
        {
            var problemDetails = new ProblemDetails
            {
                Title = "Scraping Error",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = scrapeEx.Message
            };
            problemDetails.Extensions["errorCategory"] = scrapeEx.Category.ToString();

            context.Result = new ObjectResult(problemDetails)
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            context.ExceptionHandled = true;
        }
        else if (context.Exception is NotSupportedException notSupported)
        {
            context.Result = new ObjectResult(notSupported.Message)
            {
                StatusCode = StatusCodes.Status501NotImplemented,
            };
            context.ExceptionHandled = true;
        }
    }
}
