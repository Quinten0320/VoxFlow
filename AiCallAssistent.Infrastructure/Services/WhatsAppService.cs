using System.Text.Json;
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
    // Twilio Content Template SIDs (Meta-approved)
    private const string SidBevestiging     = "HX2e527da156d40bafa604eba5a9d9e9b6"; // voxflow_bevestiging
    private const string SidHerinnering24u  = "HX3a0015dfaf53f680afd38c6352a4104e"; // voxflow_herinnering_24u
    private const string SidHerrinneringDag = "HXef0336975c2d105ec896ee3a916cfe4c"; // voxflow_herinnering_dag
    private const string SidTerugbel        = "HXb5277efb45310aa7c36e021dd48d4745"; // voxflow_terugbel
    private const string SidFollowup        = "HX3e58e208614b963df6ec011d257a85f2"; // voxflow_followup

    // ── Low-level send (Twilio Content Templates API) ────────────────────────

    private async Task SendTemplateAsync(string toNumber, string fromNumber,
        string contentSid, Dictionary<string, string> variables)
    {
        var settings = twilioSettings.Value;
        var contentVariables = JsonSerializer.Serialize(variables);

        try
        {
            var client = httpClientFactory.CreateClient("Twilio");
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json";

            var response = await client.PostAsync(url, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["From"]             = $"whatsapp:{fromNumber}",
                    ["To"]               = $"whatsapp:{toNumber}",
                    ["ContentSid"]       = contentSid,
                    ["ContentVariables"] = contentVariables,
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

    private async Task SendForCompanyAsync(short companyId, string toNumber,
        string contentSid, Dictionary<string, string> variables, string messageType)
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

        await SendTemplateAsync(toNumber, settings.WhatsAppPhoneNumber, contentSid, variables);

        db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
        {
            CompanyId   = companyId,
            MessageType = messageType,
            ToNumber    = toNumber,
            SentAt      = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ── Typed message senders ─────────────────────────────────────────────────

    public Task SendAppointmentConfirmationAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        return SendForCompanyAsync(companyId, toNumber, SidBevestiging,
            new Dictionary<string, string>
            {
                ["1"] = callerName ?? "daar",
                ["2"] = companyName,
                ["3"] = nl.ToString("dddd d MMMM", new System.Globalization.CultureInfo("nl-NL")),
                ["4"] = nl.ToString("HH:mm"),
            }, "confirmation");
    }

    public Task SendAppointmentReminderAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        return SendForCompanyAsync(companyId, toNumber, SidHerinnering24u,
            new Dictionary<string, string>
            {
                ["1"] = callerName ?? "daar",
                ["2"] = nl.ToString("HH:mm"),
                ["3"] = companyName,
            }, "reminder");
    }

    public Task SendAppointmentDayReminderAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType)
    {
        var nl = NlTimeZone.ConvertFromUtc(startTime);
        return SendForCompanyAsync(companyId, toNumber, SidHerrinneringDag,
            new Dictionary<string, string>
            {
                ["1"] = callerName ?? "daar",
                ["2"] = nl.ToString("HH:mm"),
                ["3"] = companyName,
            }, "day_reminder");
    }

    public Task SendCallbackConfirmationAsync(short companyId, string toNumber,
        string? callerName)
    {
        return SendForCompanyAsync(companyId, toNumber, SidTerugbel,
            new Dictionary<string, string>
            {
                ["1"] = callerName ?? "daar",
            }, "callback");
    }

    public Task SendAppointmentFollowupAsync(short companyId, string toNumber,
        string companyName, string? callerName)
    {
        return SendForCompanyAsync(companyId, toNumber, SidFollowup,
            new Dictionary<string, string>
            {
                ["1"] = callerName ?? "daar",
                ["2"] = companyName,
            }, "followup");
    }
}
