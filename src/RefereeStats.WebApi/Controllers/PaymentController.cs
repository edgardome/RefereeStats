using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RefereeStats.WebApi.Services;
using Stripe;
using Stripe.BillingPortal;
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
        
        var options = new Stripe.Checkout.SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
                AllowPromotionCodes = true,
                LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = 499, // 4.99€
                        Currency = "eur",
                        Product = "prod_UyUbrpS68PUSBZ",
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

        var service = new Stripe.Checkout.SessionService();
        Stripe.Checkout.Session session = service.Create(options);

        return Ok(new { url = session.Url });
    }

    [HttpPost("portal")]
    [Authorize]
    public ActionResult CreatePortalSession()
    {
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value 
                 ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        
        if (string.IsNullOrEmpty(email)) return Unauthorized();

        var user = _authService.GetUserByEmail(email);
        if (user == null) return Unauthorized();

        var customerId = user.StripeCustomerId;

        // If no customerId or pending, try to look it up in Stripe by email
        if (string.IsNullOrEmpty(customerId) || customerId == "PENDING_STRIPE_SYNC")
        {
            var customerService = new CustomerService();
            var customers = customerService.List(new CustomerListOptions { Email = email, Limit = 1 });
            var stripeCustomer = customers.FirstOrDefault();
            if (stripeCustomer != null)
            {
                customerId = stripeCustomer.Id;
                // Save it back so future lookups are instant
                _authService.UpgradeUser(email, customerId);
                _logger.LogInformation("Synced StripeCustomerId {Id} for {Email}", customerId, email);
            }
        }

        if (string.IsNullOrEmpty(customerId))
        {
            return BadRequest(new { message = "No active subscription found in Stripe." });
        }

        var domain = $"{Request.Scheme}://{Request.Host}";
        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = domain + "/index.html",
        };
        var service = new Stripe.BillingPortal.SessionService();
        var session = service.Create(options);

        return Ok(new { url = session.Url });
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
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
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
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
                    
                    // Fetch the customer to get their email as a fallback
                    string customerEmail = null;
                    try 
                    {
                        var custService = new CustomerService();
                        var customer = custService.Get(subscription.CustomerId);
                        customerEmail = customer?.Email;
                    } 
                    catch (Exception ex) 
                    {
                        _logger.LogWarning(ex, "Failed to fetch customer email from Stripe for downgrade fallback");
                    }

                    _authService.DowngradeUser(subscription.CustomerId, customerEmail);
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
