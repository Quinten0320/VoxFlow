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
    public async Task SendAsync(string toNumber, string message)
    {
        var settings = twilioSettings.Value;

        if (string.IsNullOrWhiteSpace(settings.WhatsAppFrom))
        {
            logger.LogDebug("WhatsApp skipped — Twilio:WhatsAppFrom is not configured");
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient("Twilio");
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json";

            var response = await client.PostAsync(url, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["From"] = $"whatsapp:{settings.WhatsAppFrom}",
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
        string toNumber, string displayName, DateTimeOffset startTime, string employeeName)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        var msg =
            $"✅ Uw afspraak is bevestigd!\n\n" +
            $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
            $"💇 {displayName}\n" +
            $"👤 {employeeName}\n\n" +
            $"Wilt u de afspraak wijzigen? Bel ons dan.";

        await SendAsync(toNumber, msg);
    }

    public async Task SendAppointmentReminderAsync(
        string toNumber, string displayName, DateTimeOffset startTime,
        string employeeName, string companyName)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        var msg =
            $"⏰ Herinnering: morgen heeft u een afspraak bij {companyName}!\n\n" +
            $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
            $"💇 {displayName}\n" +
            $"👤 {employeeName}";

        await SendAsync(toNumber, msg);
    }
}
