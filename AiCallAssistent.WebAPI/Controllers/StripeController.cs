using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/stripe")]
public class StripeController(
    AppDbContext db,
    IOptions<StripeSettings> stripeOptions) : DashboardControllerBase(db)
{
    private readonly StripeSettings _stripe = stripeOptions.Value;

    // ── Checkout Session ──────────────────────────────────────────────────────

    [HttpPost("checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var priceId = ResolvePriceId(request.PlanId, request.Interval);
        if (priceId is not { Length: > 0 })
            return BadRequest(new { error = "Onbekend pakket of interval." });

        var origin = $"{Request.Scheme}://{Request.Host}";

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price    = priceId,
                    Quantity = 1,
                }
            ],
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                TrialPeriodDays = 14,
                Metadata        = new Dictionary<string, string> { ["company_id"] = companyId.ToString() },
            },
            Metadata = new Dictionary<string, string>
            {
                ["company_id"]   = companyId.ToString(),
                ["plan_id"]      = request.PlanId,
                ["interval"]     = request.Interval,
            },
            PaymentMethodCollection = "always",
            SuccessUrl = $"{origin}/onboarding?stripe_success=1",
            CancelUrl  = $"{origin}/onboarding?step=7",
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url });
    }

    // ── Webhook ───────────────────────────────────────────────────────────────

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        var payload   = await new StreamReader(Request.Body).ReadToEndAsync();
        var sigHeader = Request.Headers["Stripe-Signature"].ToString();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, sigHeader, _stripe.WebhookSecret);
        }
        catch (StripeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompleted(stripeEvent);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdated(stripeEvent);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeleted(stripeEvent);
                break;
        }

        return Ok();
    }

    // ── Customer Portal ───────────────────────────────────────────────────────

    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortalSession()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var pkg = await Db.CompanyPackages.FirstOrDefaultAsync(p => p.CompanyId == companyId);
        if (pkg?.StripeCustomerId == null)
            return BadRequest(new { error = "Geen actief Stripe-abonnement gevonden." });

        var origin = $"{Request.Scheme}://{Request.Host}";

        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer  = pkg.StripeCustomerId,
            ReturnUrl = $"{origin}/dashboard/instellingen",
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url });
    }

    // ── Webhook handlers ──────────────────────────────────────────────────────

    private async Task HandleCheckoutCompleted(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null) return;

        if (!session.Metadata.TryGetValue("company_id", out var companyIdStr)
            || !short.TryParse(companyIdStr, out var companyId))
            return;

        var planId   = session.Metadata.GetValueOrDefault("plan_id");
        var interval = session.Metadata.GetValueOrDefault("interval");

        // Fetch the subscription to get trial end date
        DateTimeOffset? trialEndsAt = null;
        DateTimeOffset? periodEnd   = null;
        if (session.SubscriptionId is { Length: > 0 })
        {
            var subService   = new SubscriptionService();
            var subscription = await subService.GetAsync(session.SubscriptionId);
            trialEndsAt = subscription.TrialEnd.HasValue
                ? new DateTimeOffset(subscription.TrialEnd.Value, TimeSpan.Zero)
                : null;
            // CurrentPeriodEnd is on SubscriptionItem in Stripe.net v47+
            var firstItem = subscription.Items?.Data?.FirstOrDefault();
            periodEnd = firstItem != null
                ? new DateTimeOffset(firstItem.CurrentPeriodEnd, TimeSpan.Zero)
                : null;
        }

        var pkg = await Db.CompanyPackages.FirstOrDefaultAsync(p => p.CompanyId == companyId);
        if (pkg == null) return;

        pkg.StripeCustomerId     = session.CustomerId;
        pkg.StripeSubscriptionId = session.SubscriptionId;
        pkg.SubscriptionStatus   = "trialing";
        pkg.TrialEndsAt          = trialEndsAt;
        pkg.CurrentPeriodEnd     = periodEnd;
        pkg.PlanName             = planId;
        pkg.BillingInterval      = interval;
        pkg.UpdatedAt            = DateTimeOffset.UtcNow;

        await Db.SaveChangesAsync();
    }

    private async Task HandleSubscriptionUpdated(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        var pkg = await Db.CompanyPackages
            .FirstOrDefaultAsync(p => p.StripeSubscriptionId == subscription.Id);
        if (pkg == null) return;

        pkg.SubscriptionStatus = subscription.Status;
        pkg.TrialEndsAt        = subscription.TrialEnd.HasValue
            ? new DateTimeOffset(subscription.TrialEnd.Value, TimeSpan.Zero)
            : null;
        var item = subscription.Items?.Data?.FirstOrDefault();
        pkg.CurrentPeriodEnd = item != null
            ? new DateTimeOffset(item.CurrentPeriodEnd, TimeSpan.Zero)
            : null;
        pkg.UpdatedAt          = DateTimeOffset.UtcNow;

        await Db.SaveChangesAsync();
    }

    private async Task HandleSubscriptionDeleted(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        var pkg = await Db.CompanyPackages
            .FirstOrDefaultAsync(p => p.StripeSubscriptionId == subscription.Id);
        if (pkg == null) return;

        pkg.SubscriptionStatus = "canceled";
        pkg.UpdatedAt          = DateTimeOffset.UtcNow;

        await Db.SaveChangesAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string? ResolvePriceId(string planId, string interval) =>
        (planId, interval) switch
        {
            ("Start", "monthly") => _stripe.StartMonthlyPriceId,
            ("Start", "yearly")  => _stripe.StartYearlyPriceId,
            ("Basis", "monthly") => _stripe.BasisMonthlyPriceId,
            ("Basis", "yearly")  => _stripe.BasisYearlyPriceId,
            ("Groei", "monthly") => _stripe.GroeiMonthlyPriceId,
            ("Groei", "yearly")  => _stripe.GroeiYearlyPriceId,
            _                    => null,
        };
}

public record CreateCheckoutSessionRequest(string PlanId, string Interval);
