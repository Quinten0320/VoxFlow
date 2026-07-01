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
    EmailTemplateService emailTemplates,
    ILogger<StripeController> logger) : DashboardControllerBase(db)
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
            return BadRequest(new {
                error = "Onbekend pakket of interval.",
                received = new { planId = request.PlanId, interval = request.Interval },
                resolved = priceId,
                configured = new {
                    startMonthly = _stripe.StartMonthlyPriceId?.Length > 0,
                    basisMonthly = _stripe.BasisMonthlyPriceId?.Length > 0,
                    groeiMonthly = _stripe.GroeiMonthlyPriceId?.Length > 0
                }
            });

        var origin = !string.IsNullOrWhiteSpace(request.FrontendOrigin)
            ? request.FrontendOrigin.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";

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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stripe webhook validation failed");
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

    // ── Subscription Status ───────────────────────────────────────────────────

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var pkg = await Db.CompanyPackages.FirstOrDefaultAsync(p => p.CompanyId == companyId);
        if (pkg == null)
            return Ok(new { status = "none", planName = (string?)null, interval = (string?)null,
                            trialEndsAt = (DateTimeOffset?)null, currentPeriodEnd = (DateTimeOffset?)null,
                            cancelAtPeriodEnd = false });

        var cancelAtPeriodEnd = false;
        string?        pendingPlanName     = null;
        string?        pendingPlanInterval = null;
        DateTimeOffset? pendingAt          = null;

        if (pkg.StripeSubscriptionId is { Length: > 0 })
        {
            try
            {
                var sub = await new SubscriptionService().GetAsync(
                    pkg.StripeSubscriptionId,
                    new SubscriptionGetOptions { Expand = ["schedule"] });

                cancelAtPeriodEnd = sub.CancelAtPeriodEnd;

                // Check for a scheduled plan change (phase 2 of a subscription schedule)
                if (sub.Schedule is SubscriptionSchedule schedule && schedule.Phases?.Count > 1)
                {
                    var nextPhase    = schedule.Phases[1];
                    var nextPriceId  = nextPhase.Items?.FirstOrDefault()?.Price?.Id;
                    if (nextPriceId != null)
                    {
                        var (resolvedPlan, resolvedInterval) = ResolvePlanFromPriceId(nextPriceId);
                        if (resolvedPlan != null)
                        {
                            pendingPlanName     = resolvedPlan;
                            pendingPlanInterval = resolvedInterval;
                            pendingAt           = nextPhase.StartDate != default
                                ? new DateTimeOffset(nextPhase.StartDate, TimeSpan.Zero)
                                : null;
                        }
                    }
                }
            }
            catch { /* Stripe unreachable — return what we have in DB */ }
        }

        return Ok(new
        {
            status              = pkg.SubscriptionStatus,
            planName            = pkg.PlanName,
            interval            = pkg.BillingInterval,
            trialEndsAt         = pkg.TrialEndsAt,
            currentPeriodEnd    = pkg.CurrentPeriodEnd,
            cancelAtPeriodEnd,
            pendingPlanName,
            pendingPlanInterval,
            pendingAt,
        });
    }

    // ── Change Plan (deferred to next billing date) ───────────────────────────

    [HttpPost("change-plan")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var pkg = await Db.CompanyPackages.FirstOrDefaultAsync(p => p.CompanyId == companyId);
        if (pkg?.StripeSubscriptionId == null)
            return BadRequest(new { error = "Geen actief abonnement gevonden." });

        var newPriceId = ResolvePriceId(request.PlanId, request.Interval);
        if (newPriceId == null)
            return BadRequest(new { error = "Onbekend pakket of interval." });

        var subService   = new SubscriptionService();
        var subscription = await subService.GetAsync(pkg.StripeSubscriptionId);
        var currentItem  = subscription.Items?.Data?.FirstOrDefault();
        if (currentItem == null)
            return BadRequest(new { error = "Geen abonnementsitem gevonden." });

        // Same plan + interval → no-op
        if (currentItem.Price?.Id == newPriceId)
            return Ok(new { immediate = false, effectiveDate = (DateTimeOffset?)null, unchanged = true });

        // During trial: change immediately (no money involved yet)
        if (pkg.SubscriptionStatus == "trialing")
        {
            await subService.UpdateAsync(pkg.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                Items = [new SubscriptionItemOptions { Id = currentItem.Id, Price = newPriceId }],
                ProrationBehavior = "none",
            });
            return Ok(new { immediate = true, effectiveDate = (DateTimeOffset?)null });
        }

        // Active: cancel any existing schedule first to avoid conflicts
        if (subscription.ScheduleId != null)
        {
            try
            {
                await new SubscriptionScheduleService().CancelAsync(
                    subscription.ScheduleId,
                    new SubscriptionScheduleCancelOptions { InvoiceNow = false, Prorate = false });
                // Re-fetch so ScheduleId is cleared
                subscription = await subService.GetAsync(pkg.StripeSubscriptionId);
                currentItem  = subscription.Items?.Data?.FirstOrDefault()!;
            }
            catch { /* ignore if schedule already released */ }
        }

        // Create schedule from current subscription state
        var scheduleService = new SubscriptionScheduleService();
        var schedule = await scheduleService.CreateAsync(new SubscriptionScheduleCreateOptions
        {
            FromSubscription = pkg.StripeSubscriptionId,
        });

        var periodEnd = currentItem.CurrentPeriodEnd;

        // Phase 1: keep current plan until end of billing period (no proration)
        // Phase 2: switch to new plan — Stripe starts this when phase 1 ends
        await scheduleService.UpdateAsync(schedule.Id, new SubscriptionScheduleUpdateOptions
        {
            EndBehavior = "release",
            Phases =
            [
                new SubscriptionSchedulePhaseOptions
                {
                    StartDate = new AnyOf<DateTime?, SubscriptionSchedulePhaseStartDate>(SubscriptionSchedulePhaseStartDate.Now),
                    EndDate   = new AnyOf<DateTime?, SubscriptionSchedulePhaseEndDate>(periodEnd),
                    Items     =
                    [
                        new SubscriptionSchedulePhaseItemOptions
                        {
                            Price    = currentItem.Price!.Id,
                            Quantity = 1,
                        }
                    ],
                    ProrationBehavior = "none",
                },
                new SubscriptionSchedulePhaseOptions
                {
                    Items =
                    [
                        new SubscriptionSchedulePhaseItemOptions
                        {
                            Price    = newPriceId,
                            Quantity = 1,
                        }
                    ],
                },
            ],
        });

        var effectiveDate = new DateTimeOffset(periodEnd, TimeSpan.Zero);
        return Ok(new { immediate = false, effectiveDate });
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
        pkg.MaxWhatsAppPerMonth  = WhatsAppLimitForPlan(planId);
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
                    var capturedLogger = logger;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var (s, h) = emailTemplates.ReferralSuccess(
                                referrerOwner.Name, referredOwner.Name);
                            await emailSender.SendNowAsync(referrerPkg.CompanyId,
                                referrerOwner.Email, referrerOwner.Name, "referral_success", s, h);
                        }
                        catch (Exception ex)
                        {
                            capturedLogger.LogError(ex, "Failed to send referral_success email to company {CompanyId}", referrerPkg.CompanyId);
                        }
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

        var prevStatus    = pkg.SubscriptionStatus;
        var oldPeriodEnd  = pkg.CurrentPeriodEnd;

        pkg.SubscriptionStatus = subscription.Status;
        pkg.TrialEndsAt        = subscription.TrialEnd.HasValue
            ? new DateTimeOffset(subscription.TrialEnd.Value, TimeSpan.Zero)
            : null;
        var item = subscription.Items?.Data?.FirstOrDefault();
        var newPeriodEnd = item != null
            ? new DateTimeOffset(item.CurrentPeriodEnd, TimeSpan.Zero)
            : (DateTimeOffset?)null;
        pkg.CurrentPeriodEnd = newPeriodEnd;
        pkg.UpdatedAt = DateTimeOffset.UtcNow;

        // Track when a subscription first enters a non-paying state (for 30-day AVG deletion)
        var isExpiredStatus = subscription.Status is "past_due" or "unpaid" or "canceled";
        if (isExpiredStatus && pkg.SubscriptionExpiredAt == null)
            pkg.SubscriptionExpiredAt = DateTimeOffset.UtcNow;
        else if (!isExpiredStatus)
            pkg.SubscriptionExpiredAt = null;

        // Update plan name / interval when the customer upgrades or downgrades
        if (item?.Price?.Id is { Length: > 0 } priceId)
        {
            var (planId, interval) = ResolvePlanFromPriceId(priceId);
            if (planId != null)
            {
                pkg.PlanName             = planId;
                pkg.BillingInterval      = interval;
                pkg.MaxWhatsAppPerMonth  = WhatsAppLimitForPlan(planId);
            }
        }

        await Db.SaveChangesAsync();

        // Overage billing: when period rolls over, bill extra minutes from the completed period.
        // Only runs when AllowOverage=true, a minute limit is set, and the subscription is active.
        var periodRolledOver = oldPeriodEnd.HasValue && newPeriodEnd.HasValue
                            && newPeriodEnd.Value > oldPeriodEnd.Value;

        if (periodRolledOver
            && pkg.AllowOverage
            && pkg.MaxCallMinutes.HasValue
            && pkg.StripeCustomerId is { Length: > 0 }
            && subscription.Status is "active" or "trialing")
        {
            var capturedLogger = logger;
            var capturedCompanyId = pkg.CompanyId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await BillOverageAsync(pkg.CompanyId, pkg.StripeCustomerId!,
                        pkg.MaxCallMinutes!.Value, pkg.BillingInterval, pkg.PlanName, oldPeriodEnd!.Value);
                }
                catch (Exception ex)
                {
                    capturedLogger.LogError(ex, "Overage billing failed for company {CompanyId}", capturedCompanyId);
                }
            });
        }
    }

    private async Task BillOverageAsync(
        short companyId, string stripeCustomerId,
        int maxCallMinutes, string? billingInterval, string? planName, DateTimeOffset oldPeriodEnd)
    {
        var ratePerMinuteCents = OverageRateForPlan(planName);
        if (ratePerMinuteCents <= 0) return;

        // Determine start of the completed billing period.
        var periodStart = billingInterval == "yearly"
            ? oldPeriodEnd.AddYears(-1)
            : oldPeriodEnd.AddMonths(-1);

        // Effective limit in seconds (yearly plan = 12× monthly limit).
        var periodLimitSeconds = (long)(billingInterval == "yearly"
            ? maxCallMinutes * 12
            : maxCallMinutes) * 60;

        var usedSeconds = await Db.CallSessions
            .Where(s => s.CompanyId == companyId
                     && s.StartedAt >= periodStart
                     && s.StartedAt < oldPeriodEnd
                     && s.DurationSeconds != null)
            .SumAsync(s => (long?)s.DurationSeconds ?? 0);

        var overageSeconds = usedSeconds - periodLimitSeconds;
        if (overageSeconds <= 0) return;

        // Bill per second, rounded up to nearest cent.
        var amountCents = (long)Math.Ceiling(overageSeconds * ratePerMinuteCents / 60.0);
        if (amountCents <= 0) return;

        var overageMin = overageSeconds / 60;
        var overageSec = overageSeconds % 60;
        var timeStr    = overageSec > 0 ? $"{overageMin} min {overageSec} sec" : $"{overageMin} min";
        var rateStr    = $"€{ratePerMinuteCents / 100m:0.00}";
        var description = $"Buitenbundel: {timeStr} × {rateStr}/min (per sec. afgerekend)";

        await new InvoiceItemService().CreateAsync(new InvoiceItemCreateOptions
        {
            Customer    = stripeCustomerId,
            Amount      = amountCents,
            Currency    = "eur",
            Description = description,
        });
    }

    // Overage rate in euro-cents per minute, matching website pricing.
    private static int OverageRateForPlan(string? planName) => planName switch
    {
        "Start" => 23,  // €0,23/min
        "Basis" => 20,  // €0,20/min
        "Groei" => 18,  // €0,18/min
        _       => 0,   // unknown plan → don't bill
    };

    private async Task HandleSubscriptionDeleted(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        var pkg = await Db.CompanyPackages
            .FirstOrDefaultAsync(p => p.StripeSubscriptionId == subscription.Id);
        if (pkg == null) return;

        pkg.SubscriptionStatus    = "canceled";
        pkg.SubscriptionExpiredAt ??= DateTimeOffset.UtcNow;
        pkg.UpdatedAt             = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync();

        var owner = await Db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == pkg.CompanyId && e.IsOwner && e.IsActive
                                   && e.Email != null);
        if (owner?.Email == null) return;

        // #13 SubscriptionCancelled
        var eindDatum = (pkg.CurrentPeriodEnd ?? DateTimeOffset.UtcNow).ToString("dd-MM-yyyy");
        var capturedLogger1 = logger;
        var capturedCompanyId1 = pkg.CompanyId;
        _ = Task.Run(async () =>
        {
            try
            {
                var (s, h) = emailTemplates.SubscriptionCancelled(owner.Name, eindDatum);
                await emailSender.SendNowAsync(pkg.CompanyId, owner.Email, owner.Name,
                    "subscription_cancelled", s, h);
            }
            catch (Exception ex)
            {
                capturedLogger1.LogError(ex, "Failed to send subscription_cancelled email to company {CompanyId}", capturedCompanyId1);
            }
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
            var capturedLogger2 = logger;
            var capturedCompanyId2 = pkg.CompanyId;
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = emailTemplates.SubscriptionStarted(
                        owner.Name, abonnement, startdatum, volgendeFactuur);
                    await emailSender.SendNowAsync(pkg.CompanyId, owner.Email!, owner.Name,
                        "subscription_started", s, h);
                }
                catch (Exception ex)
                {
                    capturedLogger2.LogError(ex, "Failed to send subscription_started email to company {CompanyId}", capturedCompanyId2);
                }
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

        // Determine referrer's monthly price
        long referrerMonthlyCents = 0;
        if (referrer.StripeSubscriptionId != null)
        {
            var subService = new SubscriptionService();
            var refSub     = await subService.GetAsync(referrer.StripeSubscriptionId);
            var item       = refSub?.Items?.Data?.FirstOrDefault();
            var unitAmount = item?.Price?.UnitAmount ?? 0;
            referrerMonthlyCents = item?.Price?.Recurring?.Interval == "year"
                ? unitAmount / 12
                : unitAmount;
        }

        // Referred's monthly price = what they paid on this invoice (already monthly for subscription_cycle)
        var referredMonthlyCents = invoice.AmountPaid;

        // Credit = min(referred price, referrer price) — capped at referrer's own monthly amount
        // so a lower-tier referrer never gets more than 1 full month of their own plan
        var creditCents = referrerMonthlyCents > 0
            ? -Math.Min(referredMonthlyCents, referrerMonthlyCents)
            : -referredMonthlyCents;

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
            var capturedLogger3 = logger;
            var capturedCompanyId3 = referrer.CompanyId;
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = emailTemplates.ReferralRewarded(referrerOwner.Name, beloningBedrag);
                    await emailSender.SendNowAsync(referrer.CompanyId, referrerOwner.Email!,
                        referrerOwner.Name, "referral_rewarded", s, h);
                }
                catch (Exception ex)
                {
                    capturedLogger3.LogError(ex, "Failed to send referral_rewarded email to company {CompanyId}", capturedCompanyId3);
                }
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

    private (string? planId, string? interval) ResolvePlanFromPriceId(string priceId)
    {
        if (priceId == _stripe.StartMonthlyPriceId) return ("Start", "monthly");
        if (priceId == _stripe.StartYearlyPriceId)  return ("Start", "yearly");
        if (priceId == _stripe.BasisMonthlyPriceId) return ("Basis", "monthly");
        if (priceId == _stripe.BasisYearlyPriceId)  return ("Basis", "yearly");
        if (priceId == _stripe.GroeiMonthlyPriceId) return ("Groei", "monthly");
        if (priceId == _stripe.GroeiYearlyPriceId)  return ("Groei", "yearly");
        return (null, null);
    }

    private static int? WhatsAppLimitForPlan(string? planId) => planId switch
    {
        "Start" => 25,
        "Basis" => 100,
        "Groei" => null,  // unlimited
        _       => null,
    };
}

public record CreateCheckoutSessionRequest(string PlanId, string Interval, string? ReferralCode, string? FrontendOrigin);
public record ChangePlanRequest(string PlanId, string Interval);
