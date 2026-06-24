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
    private async Task SendAsync(string toNumber, string message, string? fromNumber = null)
    {
        var settings = twilioSettings.Value;
        var sender = fromNumber ?? settings.WhatsAppFrom;

        if (string.IsNullOrWhiteSpace(sender))
        {
            logger.LogDebug("WhatsApp skipped — no sender number configured");
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient("Twilio");
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json";

            var response = await client.PostAsync(url, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["From"] = $"whatsapp:{sender}",
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

    private async Task SendAppointmentConfirmationAsync(
        string toNumber, string displayName, DateTimeOffset startTime, string employeeName,
        string? fromNumber = null)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        var msg =
            $"✅ Uw afspraak is bevestigd!\n\n" +
            $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
            $"💇 {displayName}\n" +
            $"👤 {employeeName}\n\n" +
            $"Wilt u de afspraak wijzigen? Bel ons dan.";

        await SendAsync(toNumber, msg, fromNumber);
    }

    private async Task SendAppointmentReminderAsync(
        string toNumber, string displayName, DateTimeOffset startTime,
        string employeeName, string companyName, string? fromNumber = null)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        var msg =
            $"⏰ Herinnering: morgen heeft u een afspraak bij {companyName}!\n\n" +
            $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
            $"💇 {displayName}\n" +
            $"👤 {employeeName}";

        await SendAsync(toNumber, msg, fromNumber);
    }

    private async Task SendCallbackConfirmationAsync(
        string toNumber, string callerName, DateTimeOffset scheduledFrom, DateTimeOffset scheduledUntil,
        string? fromNumber = null)
    {
        var fromNl = NlTimeZone.ConvertFromUtc(scheduledFrom);
        var untilNl = NlTimeZone.ConvertFromUtc(scheduledUntil);
        var msg =
            $"📞 Terugbelverzoek ontvangen, {callerName}!\n\n" +
            $"Wij bellen u terug op {fromNl:dddd d MMMM} tussen {fromNl:HH:mm} en {untilNl:HH:mm}.\n\n" +
            $"Staat u ergens anders voor open? Bel ons dan even.";

        await SendAsync(toNumber, msg, fromNumber);
    }

    private Task SendAppointmentDayReminderAsync(
        string toNumber, string displayName, DateTimeOffset startTime,
        string employeeName, string companyName, string? fromNumber = null)
        => SendAppointmentReminderAsync(toNumber, displayName, startTime, employeeName, companyName, fromNumber);

    private async Task SendAppointmentFollowupAsync(
        string toNumber, string displayName, string companyName, string? fromNumber = null)
    {
        var msg =
            $"😊 Bedankt voor uw bezoek bij {companyName}!\n\n" +
            $"We hopen dat uw {displayName} naar wens was. Tot de volgende keer!";

        await SendAsync(toNumber, msg, fromNumber);
    }

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

        await SendAsync(toNumber, message, settings.WhatsAppPhoneNumber);

        db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
        {
            CompanyId   = companyId,
            MessageType = messageType,
            ToNumber    = toNumber,
            SentAt      = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
