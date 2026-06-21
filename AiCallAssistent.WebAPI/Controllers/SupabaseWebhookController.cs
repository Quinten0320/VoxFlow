using System.Text.Json;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

/// <summary>
/// Receives Supabase database webhooks for auth events (SIGNED_UP, PASSWORD_RECOVERY).
/// Configure in Supabase: Database → Webhooks → Create a new hook for the auth.users table
/// with header "X-Webhook-Secret: <SupabaseWebhookSecret from config>".
/// </summary>
[Route("api/supabase")]
[AllowAnonymous]
[ApiController]
public class SupabaseWebhookController(
    AppDbContext db,
    EmailSender emailSender,
    EmailTemplateService templates,
    IConfiguration config,
    ILogger<SupabaseWebhookController> logger) : ControllerBase
{
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        // Verify shared secret to ensure the request is from Supabase
        var expectedSecret = config["SupabaseWebhookSecret"] ?? config["Supabase:WebhookSecret"];
        var receivedSecret = Request.Headers["X-Webhook-Secret"].ToString();

        if (string.IsNullOrEmpty(expectedSecret) || receivedSecret != expectedSecret)
            return Unauthorized(new { error = "Invalid webhook secret" });

        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch
        {
            return BadRequest(new { error = "Invalid JSON" });
        }

        // Supabase database webhooks have { "type": "INSERT"|"UPDATE", "table": "users", "record": {...} }
        var eventType = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        var record    = root.TryGetProperty("record", out var r) ? r : default;

        if (eventType == null || record.ValueKind == JsonValueKind.Undefined)
            return Ok();

        var userId    = record.TryGetProperty("id", out var id) ? id.GetString() : null;
        var userEmail = record.TryGetProperty("email", out var em) ? em.GetString() : null;

        if (userId == null || userEmail == null) return Ok();

        // Resolve display name: try user_metadata.name, fall back to email prefix
        var userName = userEmail.Split('@')[0];
        if (record.TryGetProperty("raw_user_meta_data", out var meta)
            && meta.TryGetProperty("name", out var nameEl)
            && nameEl.GetString() is { Length: > 0 } n)
            userName = n;
        else if (record.TryGetProperty("raw_user_meta_data", out var meta2)
            && meta2.TryGetProperty("full_name", out var fnEl)
            && fnEl.GetString() is { Length: > 0 } fn)
            userName = fn;

        switch (eventType)
        {
            case "INSERT":
                await HandleSignedUp(userId, userEmail, userName);
                break;

            case "UPDATE":
                // PASSWORD_RECOVERY completion: recovery_sent_at is set
                if (record.TryGetProperty("recovery_sent_at", out _))
                    await HandlePasswordRecovery(userEmail, userName);
                break;
        }

        return Ok();
    }

    private async Task HandleSignedUp(string authUserId, string email, string name)
    {
        logger.LogInformation("New user signed up: {Email}", email);

        // Try to resolve the company the employee belongs to
        short? companyId = null;
        if (Guid.TryParse(authUserId, out var authGuid))
        {
            companyId = await db.Employees
                .Where(e => e.AuthUserId == authGuid && e.IsActive)
                .Select(e => (short?)e.CompanyId)
                .FirstOrDefaultAsync();
        }

        // #2 Welcome email
        try
        {
            var (s, h) = templates.Welcome(name);
            await emailSender.SendNowAsync(companyId, email, name, "welcome", s, h);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send welcome email to {Email}", email);
        }

        // Schedule #6 OnboardingStart (+1 day) and #7 OnboardingReminder (+3 days)
        if (companyId.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            await emailSender.ScheduleAsync(companyId.Value, "onboarding_start",
                new { }, now.AddDays(1));
            await emailSender.ScheduleAsync(companyId.Value, "onboarding_reminder",
                new { }, now.AddDays(3));
        }
    }

    private async Task HandlePasswordRecovery(string email, string name)
    {
        logger.LogInformation("Password recovery for {Email}", email);

        var companyId = await db.Employees
            .Where(e => e.Email == email && e.IsActive)
            .Select(e => (short?)e.CompanyId)
            .FirstOrDefaultAsync();

        try
        {
            var tijdstip = DateTimeOffset.UtcNow.ToString("dd-MM-yyyy HH:mm");
            var (s, h) = templates.PasswordResetConfirmed(name, tijdstip);
            await emailSender.SendNowAsync(companyId, email, name, "password_reset_confirmed", s, h);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", email);
        }
    }
}
