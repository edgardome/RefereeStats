using Microsoft.AspNetCore.Mvc;
using RefereeStats.WebApi.Services;
using Stripe;
using Stripe.Checkout;

namespace RefereeStats.WebApi.Controllers;

public class CheckoutRequest
{
    public required string Email { get; set; }
}

[ApiController]
[Route("api/v1/payments")]
public sealed class PaymentController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly AuthService _authService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(IConfiguration configuration, AuthService authService, ILogger<PaymentController> logger)
    {
        _configuration = configuration;
        _authService = authService;
        _logger = logger;
        StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"] ?? "sk_test_mock"; // Fallback mock key
    }

    [HttpPost("checkout")]
    public ActionResult CreateCheckoutSession([FromBody] CheckoutRequest request)
    {
        // Si no hay clave válida, devolvemos un mock local para que el frontend lo gestione visualmente o lance un error.
        if (StripeConfiguration.ApiKey == "sk_test_mock")
        {
            // MOCK MODE: Upgrade directly without Stripe
            _logger.LogWarning("MOCK PAYMENT: Upgrading user {Email} automatically without real payment.", request.Email);
            _authService.UpgradeUser(request.Email, "cus_mock_" + Guid.NewGuid().ToString("N"));
            return Ok(new { url = "/index.html?payment=mock_success" });
        }

        var domain = $"{Request.Scheme}://{Request.Host}";
        
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = 499, // 4.99€
                        Currency = "eur",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "RefereeStats Premium Analytics",
                            Description = "Access to full cross-league stats, Matchup Predictor and historical data."
                        },
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = "month"
                        }
                    },
                    Quantity = 1,
                },
            },
            Mode = "subscription",
            SuccessUrl = domain + "/index.html?session_id={CHECKOUT_SESSION_ID}&payment=success",
            CancelUrl = domain + "/index.html?payment=cancelled",
            ClientReferenceId = request.Email // Usamos el email para linkarlo luego en el webhook
        };

        var service = new SessionService();
        Session session = service.Create(options);

        return Ok(new { url = session.Url });
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var endpointSecret = _configuration["Stripe:WebhookSecret"];

        try
        {
            var stripeEvent = string.IsNullOrEmpty(endpointSecret) 
                ? EventUtility.ParseEvent(json) 
                : EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], endpointSecret);

            if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
            {
                var session = stripeEvent.Data.Object as Session;
                if (session != null && !string.IsNullOrEmpty(session.ClientReferenceId))
                {
                    _logger.LogInformation("Checkout Session Completed for {Email}. Customer ID: {Customer}", session.ClientReferenceId, session.CustomerId);
                    _authService.UpgradeUser(session.ClientReferenceId, session.CustomerId);
                }
            }
            else if (stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted)
            {
                var subscription = stripeEvent.Data.Object as Subscription;
                if (subscription != null)
                {
                    _logger.LogInformation("Subscription {Id} deleted for customer {Customer}", subscription.Id, subscription.CustomerId);
                    // Aquí habría que hacer un DowngradeUser buscando por CustomerId
                }
            }

            return Ok();
        }
        catch (StripeException e)
        {
            _logger.LogError(e, "Stripe Webhook Error");
            return BadRequest();
        }
    }
}
