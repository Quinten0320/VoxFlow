using AiCallAssistent.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiCallAssistent.WebAPI.Controllers;

/// <summary>
/// Development-only endpoints to preview and send test emails.
/// GET  /dev/email-preview/{emailType}        — render HTML in browser
/// POST /dev/email-preview/{emailType}/send   — actually send via SMTP
/// </summary>
[Route("dev/email-preview")]
[AllowAnonymous]
[ApiController]
public class EmailPreviewController(
    EmailTemplateService templates,
    EmailSender emailSender,
    IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("{emailType}")]
    public IActionResult Preview(string emailType)
    {
        if (!env.IsDevelopment()) return NotFound();
        var (_, html) = Render(emailType);
        return Content(html, "text/html");
    }

    /// <summary>
    /// Sends a real email for the given template type to the specified address.
    /// Only available in Development. Defaults to the configured sender address.
    /// </summary>
    [HttpPost("{emailType}/send")]
    public async Task<IActionResult> Send(string emailType, [FromQuery] string to = "quintenwit41@gmail.com")
    {
        if (!env.IsDevelopment()) return NotFound();

        var (subject, html) = Render(emailType);
        if (string.IsNullOrEmpty(subject))
            return BadRequest(new { error = $"Onbekend email type: '{emailType}'" });

        await emailSender.SendNowAsync(null, to, "Test", emailType, subject, html);
        return Ok(new { sent = true, to, subject, emailType });
    }

    private (string Subject, string Html) Render(string emailType) =>
        emailType.ToLower() switch
        {
            "welcome"                  => templates.Welcome("Jan de Vries"),
            "password_reset_confirmed" => templates.PasswordResetConfirmed("Jan de Vries", "17-06-2026 14:32"),
            "onboarding_start"         => templates.OnboardingStart("Jan de Vries"),
            "onboarding_reminder"      => templates.OnboardingReminder("Jan de Vries"),
            "setup_complete"           => templates.SetupComplete("Jan de Vries"),
            "setup_error"              => templates.SetupError("Jan de Vries", "Twilio-telefoonnummer kon niet worden geverifieerd."),
            "subscription_started"     => templates.SubscriptionStarted("Jan de Vries", "Basis", "17-06-2026", "17-07-2026"),
            "subscription_upgraded"    => templates.SubscriptionUpgraded("Jan de Vries", "Groei", ["Onbeperkte gesprekken", "Prioriteit support"]),
            "subscription_downgraded"  => templates.SubscriptionDowngraded("Jan de Vries", "Start", "01-07-2026", ["WhatsApp integratie", "Meerdere bellers"]),
            "subscription_cancelled"   => templates.SubscriptionCancelled("Jan de Vries", "17-07-2026"),
            "account_restricted"       => templates.AccountRestricted("Jan de Vries", "€29,90"),
            "weekly_report"            => templates.WeeklyReport("Jan de Vries", 42, 8, 3, "2:14", 25),
            "monthly_report"           => templates.MonthlyReport("Jan de Vries", "mei 2026", 183, "dinsdag", "+12%", "4.8/5"),
            "inactive_7"               => templates.Inactive7Days("Jan de Vries"),
            "inactive_14"              => templates.Inactive14Days("Jan de Vries", 37),
            "inactive_30"              => templates.Inactive30Days("Jan de Vries"),
            "re_activated"             => templates.ReActivated("Jan de Vries"),
            "feedback_request"         => templates.FeedbackRequest("Jan de Vries", "TKT-1042"),
            "new_login"                => templates.NewLoginDetected("Jan de Vries", "Chrome op Windows", "85.144.12.55", "17-06-2026 09:45"),
            "account_locked"           => templates.AccountLocked("Jan de Vries", "17-06-2026 09:45"),
            "winback_1"                => templates.WinBack1("Jan de Vries", 37),
            "winback_7"                => templates.WinBack7("Jan de Vries", 25, "01-07-2026"),
            "winback_30"               => templates.WinBack30("Jan de Vries"),
            "team_member_removed"      => templates.TeamMemberRemoved("Pieter Bakker", "De Vries Loodgieters"),
            "integration_connected"    => templates.IntegrationConnected("Jan de Vries", "Microsoft Outlook"),
            "integration_error"        => templates.IntegrationError("Jan de Vries", "Microsoft Outlook", "401 Unauthorized"),
            "integration_expired"      => templates.IntegrationExpired("Jan de Vries", "Microsoft Outlook"),
            "referral_sent"            => templates.ReferralSent("Jan de Vries", "collega@bedrijf.nl"),
            "referral_success"         => templates.ReferralSuccess("Jan de Vries", "Kees Smit"),
            "referral_rewarded"        => templates.ReferralRewarded("Jan de Vries", "€29,90"),
            _                          => (string.Empty, "<h1>Onbekend email type</h1>"),
        };
}
