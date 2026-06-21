using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class WhatsAppService(
    IHttpClientFactory httpClientFactory,
    IOptions<TwilioSettings> twilioSettings,
    ILogger<WhatsAppService> logger) : IWhatsAppService
{
    public async Task SendAsync(string toNumber, string message, string? fromNumber = null)
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

    public async Task SendAppointmentConfirmationAsync(
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

    public async Task SendAppointmentReminderAsync(
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

    public async Task SendCallbackConfirmationAsync(
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

    public Task SendAppointmentDayReminderAsync(
        string toNumber, string displayName, DateTimeOffset startTime,
        string employeeName, string companyName, string? fromNumber = null)
        => SendAppointmentReminderAsync(toNumber, displayName, startTime, employeeName, companyName, fromNumber);

    public async Task SendAppointmentFollowupAsync(
        string toNumber, string displayName, string companyName, string? fromNumber = null)
    {
        var msg =
            $"😊 Bedankt voor uw bezoek bij {companyName}!\n\n" +
            $"We hopen dat uw {displayName} naar wens was. Tot de volgende keer!";

        await SendAsync(toNumber, msg, fromNumber);
    }
}
