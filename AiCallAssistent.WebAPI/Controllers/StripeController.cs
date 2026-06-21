using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services.Email;
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
    IOptions<StripeSettings> stripeOptions,
    EmailSender emailSender,
    EmailTemplateService emailTemplates) : DashboardControllerBase(db)
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
                ["company_id"]    = companyId.ToString(),
                ["plan_id"]       = request.PlanId,
                ["interval"]      = request.Interval,
                ["referral_code"] = request.ReferralCode ?? "",
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
        catch (StripeException)
        {
            return BadRequest(new { error = "Invalid webhook signature" });
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

            case "invoice.paid":
                await HandleInvoicePaid(stripeEvent);
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

        // Link referrer if a referral code was provided
        if (session.Metadata.TryGetValue("referral_code", out var refCode) && refCode?.Length > 0)
        {
            var referrerPkg = await Db.CompanyPackages
                .FirstOrDefaultAsync(p => p.ReferralCode == refCode);
            if (referrerPkg != null && referrerPkg.CompanyId != companyId)
            {
                pkg.ReferredByCompanyId = referrerPkg.CompanyId;
                await Db.SaveChangesAsync();

                // #54 ReferralSuccess — tell the referrer their friend just signed up
                var referrerOwner = await Db.Employees
                    .FirstOrDefaultAsync(e => e.CompanyId == referrerPkg.CompanyId
                                           && e.IsOwner && e.IsActive && e.Email != null);
                var referredOwner = await Db.Employees
                    .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.IsOwner && e.IsActive);
                if (referrerOwner?.Email != null && referredOwner != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var (s, h) = emailTemplates.ReferralSuccess(
                                referrerOwner.Name, referredOwner.Name);
                            await emailSender.SendNowAsync(referrerPkg.CompanyId,
                                referrerOwner.Email, referrerOwner.Name, "referral_success", s, h);
                        }
                        catch { }
                    });
                }
            }
        }
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

        var owner = await Db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == pkg.CompanyId && e.IsOwner && e.IsActive
                                   && e.Email != null);
        if (owner?.Email == null) return;

        // #13 SubscriptionCancelled
        var eindDatum = (pkg.CurrentPeriodEnd ?? DateTimeOffset.UtcNow).ToString("dd-MM-yyyy");
        _ = Task.Run(async () =>
        {
            try
            {
                var (s, h) = emailTemplates.SubscriptionCancelled(owner.Name, eindDatum);
                await emailSender.SendNowAsync(pkg.CompanyId, owner.Email, owner.Name,
                    "subscription_cancelled", s, h);
            }
            catch { }
        });

        // Schedule win-back sequence
        var now         = DateTimeOffset.UtcNow;
        var callCount   = await Db.CallSessions.CountAsync(c => c.CompanyId == pkg.CompanyId);
        var geldigTot   = now.AddDays(14).ToString("dd-MM-yyyy");
        await emailSender.ScheduleAsync(pkg.CompanyId, "winback_1",
            new { aantalGesprekken = callCount }, now.AddDays(1));
        await emailSender.ScheduleAsync(pkg.CompanyId, "winback_7",
            new { kortingsPercentage = 25, aanbiedingGeldigTot = geldigTot }, now.AddDays(7));
        await emailSender.ScheduleAsync(pkg.CompanyId, "winback_30",
            new { }, now.AddDays(30));
    }

    private async Task HandleInvoicePaid(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null || invoice.AmountPaid == 0) return;

        // Look up the company package for this customer
        var pkg = await Db.CompanyPackages
            .FirstOrDefaultAsync(p => p.StripeCustomerId == invoice.CustomerId);
        if (pkg == null) return;

        var owner = await Db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == pkg.CompanyId && e.IsOwner && e.IsActive
                                   && e.Email != null);

        // #10 SubscriptionStarted — first real payment after trial
        if (invoice.BillingReason == "subscription_cycle" && owner?.Email != null
            && !await emailSender.AlreadySentAsync(pkg.CompanyId, "subscription_started", TimeSpan.FromDays(3650)))
        {
            var abonnement     = pkg.PlanName ?? "VoxFlow";
            var startdatum     = DateTimeOffset.UtcNow.ToString("dd-MM-yyyy");
            var volgendeFactuur = (pkg.CurrentPeriodEnd ?? DateTimeOffset.UtcNow.AddMonths(1))
                                    .ToString("dd-MM-yyyy");
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = emailTemplates.SubscriptionStarted(
                        owner.Name, abonnement, startdatum, volgendeFactuur);
                    await emailSender.SendNowAsync(pkg.CompanyId, owner.Email!, owner.Name,
                        "subscription_started", s, h);
                }
                catch { }
            });
        }

        // Referral reward — only on subscription_cycle, first payment
        if (invoice.BillingReason != "subscription_cycle") return;

        var referralPkg = await Db.CompanyPackages
            .FirstOrDefaultAsync(p => p.StripeCustomerId == invoice.CustomerId
                                   && p.ReferredByCompanyId != null
                                   && p.ReferralRewardedAt == null);
        if (referralPkg == null) return;

        var referrer = await Db.CompanyPackages
            .FirstOrDefaultAsync(p => p.CompanyId == referralPkg.ReferredByCompanyId
                                   && p.StripeCustomerId != null);
        if (referrer?.StripeCustomerId == null) return;

        // Fetch the referrer's actual subscription to credit exactly 1 month's price
        long creditCents;
        if (referrer.StripeSubscriptionId != null)
        {
            var subService = new SubscriptionService();
            var refSub     = await subService.GetAsync(referrer.StripeSubscriptionId);
            var item       = refSub?.Items?.Data?.FirstOrDefault();
            var unitAmount = item?.Price?.UnitAmount ?? 0;
            // For yearly plans credit 1/12th; for monthly plans credit the full amount
            creditCents = item?.Price?.Recurring?.Interval == "year"
                ? -(unitAmount / 12)
                : -unitAmount;
        }
        else
        {
            creditCents = -invoice.AmountPaid; // fallback: credit what the referred person paid
        }

        if (creditCents == 0) return;

        var balanceService = new CustomerBalanceTransactionService();
        await balanceService.CreateAsync(referrer.StripeCustomerId,
            new CustomerBalanceTransactionCreateOptions
            {
                Amount      = creditCents,
                Currency    = "eur",
                Description = "Referral bonus – 1 maand gratis",
            });

        referralPkg.ReferralRewardedAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync();

        // #55 ReferralRewarded — tell the referrer they got their credit
        var referrerOwner = await Db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == referrer.CompanyId && e.IsOwner && e.IsActive
                                   && e.Email != null);
        if (referrerOwner?.Email != null)
        {
            var beloningBedrag = (Math.Abs(creditCents) / 100m)
                .ToString("€#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = emailTemplates.ReferralRewarded(referrerOwner.Name, beloningBedrag);
                    await emailSender.SendNowAsync(referrer.CompanyId, referrerOwner.Email!,
                        referrerOwner.Name, "referral_rewarded", s, h);
                }
                catch { }
            });
        }
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

public record CreateCheckoutSessionRequest(string PlanId, string Interval, string? ReferralCode);
