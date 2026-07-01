using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class WhatsAppService(
    IHttpClientFactory httpClientFactory,
    IOptions<TwilioSettings> twilioSettings,
    ILogger<WhatsAppService> logger,
    AppDbContext db) : IWhatsAppService
{
    // ── Low-level send (Twilio API) ──────────────────────────────────────────

    private async Task SendRawAsync(string toNumber, string message, string fromNumber)
    {
        var settings = twilioSettings.Value;

        try
        {
            var client = httpClientFactory.CreateClient("Twilio");
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json";

            var response = await client.PostAsync(url, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["From"] = $"whatsapp:{fromNumber}",
                    ["To"]   = $"whatsapp:{toNumber}",
                    ["Body"] = message
                }));

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogError("WhatsApp send failed ({Status}) to {To}: {Body}",
                    (int)response.StatusCode, toNumber, body);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WhatsApp send threw for {To}", toNumber);
        }
    }

    // ── Company-aware send (quota + active check + logging) ──────────────────

    public async Task SendForCompanyAsync(short companyId, string toNumber, string message, string messageType)
    {
        var settings = await db.AssistantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (settings is null || !settings.WhatsAppActive)
        {
            logger.LogDebug("WhatsApp skipped for company {CompanyId} — not active", companyId);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.WhatsAppPhoneNumber))
        {
            logger.LogDebug("WhatsApp skipped for company {CompanyId} — no sender number", companyId);
            return;
        }

        var pkg = await db.CompanyPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId);

        if (pkg?.MaxWhatsAppPerMonth is not null)
        {
            var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var sentThisMonth = await db.WhatsAppMessageLogs
                .CountAsync(l => l.CompanyId == companyId && l.SentAt >= monthStart);

            if (sentThisMonth >= pkg.MaxWhatsAppPerMonth.Value)
            {
                logger.LogWarning("WhatsApp quota exceeded for company {CompanyId} ({Count}/{Limit})",
                    companyId, sentThisMonth, pkg.MaxWhatsAppPerMonth.Value);
                return;
            }
        }

        await SendRawAsync(toNumber, message, settings.WhatsAppPhoneNumber);

        db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
        {
            CompanyId   = companyId,
            MessageType = messageType,
            ToNumber    = toNumber,
            SentAt      = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ── Typed message senders — single place for all message content ──────────

    public Task SendAppointmentConfirmationAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        var greeting = callerName is { Length: > 0 } ? $"Hoi {callerName}!" : "Hoi!";
        var msg =
            $"{greeting}\n\n" +
            $"Je afspraak bij {companyName} is bevestigd op {nl:dddd d MMMM} om {nl:HH:mm}.\n\n" +
            $"Wil je de afspraak wijzigen of annuleren? Bel ons gerust of stuur een bericht terug.\n\n" +
            $"Tot dan!";
        return SendForCompanyAsync(companyId, toNumber, msg, "confirmation");
    }

    public Task SendAppointmentReminderAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        var greeting = callerName is { Length: > 0 } ? $"Hoi {callerName}," : "Hoi,";
        var msg =
            $"{greeting}\n\n" +
            $"Een korte herinnering: morgen om {nl:HH:mm} heb je een afspraak bij {companyName}.\n\n" +
            $"Kun je niet? Laat het ons even weten, dan plannen we een nieuw moment in.\n\n" +
            $"Tot dan!";
        return SendForCompanyAsync(companyId, toNumber, msg, "reminder");
    }

    public Task SendAppointmentDayReminderAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        var greeting = callerName is { Length: > 0 } ? $"Hoi {callerName}," : "Hoi,";
        var msg =
            $"{greeting}\n\n" +
            $"Vandaag om {nl:HH:mm} heb je een afspraak bij {companyName}. We zien je graag!\n\n" +
            $"Kom je toch niet? Laat het ons even weten.\n\n" +
            $"Tot dan!";
        return SendForCompanyAsync(companyId, toNumber, msg, "day_reminder");
    }

    public Task SendCallbackConfirmationAsync(short companyId, string toNumber,
        string? callerName)
    {
        var greeting = callerName is { Length: > 0 } ? $"Hoi {callerName}," : "Hoi,";
        var msg =
            $"{greeting}\n\n" +
            $"Bedankt voor het bellen. We hebben je terugbelverzoek ontvangen en nemen zo snel mogelijk contact met je op.\n\n" +
            $"Heb je ondertussen een vraag? Stuur gerust een bericht.\n\n" +
            $"Tot dan!";
        return SendForCompanyAsync(companyId, toNumber, msg, "callback");
    }

    public Task SendAppointmentFollowupAsync(short companyId, string toNumber,
        string companyName, string? callerName)
    {
        var greeting = callerName is { Length: > 0 } ? $"Hoi {callerName}," : "Hoi,";
        var msg =
            $"{greeting}\n\n" +
            $"Bedankt voor je telefoontje naar {companyName} zojuist. Kunnen we nog ergens mee helpen?\n\n" +
            $"Voor vragen of een nieuwe afspraak staan we voor je klaar.";
        return SendForCompanyAsync(companyId, toNumber, msg, "followup");
    }
}
