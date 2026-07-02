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
            "onboarding_reminder"      => templates.OnboardingReminder("Jan de Vries"),
            "setup_complete"           => templates.SetupComplete("Jan de Vries"),
            "setup_error"              => templates.SetupError("Jan de Vries", "Twilio-telefoonnummer kon niet worden geverifieerd."),
            "phone_number_not_linked"  => templates.PhoneNumberNotLinked("Jan de Vries"),
            "first_call_succeeded"     => templates.FirstCallSucceeded("Jan de Vries"),
            "assistant_settings_incomplete" => templates.AssistantSettingsIncomplete("Jan de Vries"),
            "trial_started"            => templates.TrialStarted("Jan de Vries"),
            "trial_expiring_soon"      => templates.TrialExpiringSoon("Jan de Vries", "30-06-2026"),
            "subscription_started"     => templates.SubscriptionStarted("Jan de Vries", "Basis", "17-06-2026", "17-07-2026"),
            "subscription_upgraded"    => templates.SubscriptionUpgraded("Jan de Vries", "Groei", ["Onbeperkte gesprekken", "Prioriteit support"]),
            "subscription_downgraded"  => templates.SubscriptionDowngraded("Jan de Vries", "Start", "01-07-2026", ["WhatsApp integratie", "Meerdere bellers"]),
            "subscription_cancelled"   => templates.SubscriptionCancelled("Jan de Vries", "17-07-2026"),
            "invoice_available"        => templates.InvoiceAvailable("Jan de Vries", "juni 2026", "29,90", "01-06-2026", "30-06-2026", "https://voxflow.nl/dashboard/facturen"),
            "payment_failed"           => templates.PaymentFailed("Jan de Vries", "29,90"),
            "account_restricted"       => templates.AccountRestricted("Jan de Vries", "29,90"),
            "first_week_summary"       => templates.FirstWeekSummary("Jan de Vries", 18, 4, 3),
            "monthly_report"           => templates.MonthlyReport("Jan de Vries", "mei 2026", 183, "dinsdag", "+12%", 24),
            "call_limit_almost"        => templates.CallLimitAlmostReached("Jan de Vries", 80, 100),
            "call_limit_reached"       => templates.CallLimitReached("Jan de Vries", 100, "01-07-2026"),
            "inactive_7"               => templates.Inactive7Days("Jan de Vries"),
            "inactive_30"              => templates.Inactive30Days("Jan de Vries", 47),
            "winback_1"                => templates.WinBack1("Jan de Vries", 37),
            "password_reset_confirmed" => templates.PasswordResetConfirmed("Jan de Vries", "17-06-2026 14:32"),
            "new_login"                => templates.NewLoginDetected("Jan de Vries", "Chrome op Windows", "85.144.12.55", "17-06-2026 09:45"),
            "account_locked"           => templates.AccountLocked("Jan de Vries", "17-06-2026 09:45"),
            "account_birthday"         => templates.AccountBirthday("Jan de Vries", 412),
            "integration_connected"    => templates.IntegrationConnected("Jan de Vries", "Microsoft Outlook"),
            "integration_error"        => templates.IntegrationError("Jan de Vries", "Microsoft Outlook", "401 Unauthorized"),
            "integration_expired"      => templates.IntegrationExpired("Jan de Vries", "Microsoft Outlook"),
            "integration_notify_confirm" => templates.IntegrationNotifyConfirm("Jan de Vries", "Exact Online"),
            "integration_now_live"     => templates.IntegrationNowLive("Jan de Vries", "Exact Online"),
            "owner_question_answered"  => templates.OwnerQuestionAnswered("De Vries Loodgieters", "+31612345678", "Klant vroeg naar de openingstijden en prijzen voor een lekkage reparatie."),
            "owner_new_lead"           => templates.OwnerNewLead("De Vries Loodgieters", "+31687654321", "Eerste contact: klant wil een offerte voor badkamerrenovatie."),
            "whatsapp_activated"       => templates.WhatsAppActivated("Jan de Vries", "+31970123456"),
            "referral_success"         => templates.ReferralSuccess("Jan de Vries", "Kees Smit"),
            "referral_rewarded"        => templates.ReferralRewarded("Jan de Vries", "29,90"),
            "whatsapp_requested_admin" => templates.WhatsAppRequestedAdmin("De Vries Loodgieters", 18, "+31970123456"),
            "integration_notify_admin" => templates.IntegrationNotifyAdmin("De Vries Loodgieters", 18, "Exact Online"),
            "team_member_removed"      => templates.TeamMemberRemoved("Pieter Bakker", "De Vries Loodgieters"),
            "subscription_expired_warning" => templates.SubscriptionExpiredWarning("Jan de Vries", 5),
            "data_deleted"             => templates.DataDeleted("Jan de Vries"),
            _                          => (string.Empty, "<h1>Onbekend email type</h1>"),
        };
}
