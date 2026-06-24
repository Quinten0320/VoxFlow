using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

/// <summary>
/// Quick manual trigger for testing outbound WhatsApp notifications.
/// Remove or secure this controller before going to production.
/// </summary>
[ApiController]
[Route("api/notification-test")]
public class NotificationTestController(
    IWhatsAppService whatsApp,
    AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(
        [FromQuery] string to,
        [FromQuery] short companyId = 13,
        [FromQuery] string type = "reminder")
    {
        if (string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "Query param 'to' is required (e.g. +31612345678)" });

        var companyName = await db.Companies
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.CompanyName)
            .FirstOrDefaultAsync() ?? $"Bedrijf {companyId}";

        var now = DateTimeOffset.UtcNow;
        var nl  = AiCallAssistent.Application.Helpers.NlTimeZone.ConvertFromUtc(now.AddDays(1));

        string msg;
        string messageType;
        switch (type.ToLowerInvariant())
        {
            case "reminder":
                msg =
                    $"⏰ Herinnering: morgen heeft u een afspraak bij {companyName}!\n\n" +
                    $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
                    $"💇 Testafspraak\n" +
                    $"👤 Test Medewerker";
                messageType = "reminder";
                break;

            case "followup":
                msg = $"😊 Bedankt voor uw bezoek bij {companyName}!\n\nWe hopen dat uw Testafspraak naar wens was. Tot de volgende keer!";
                messageType = "followup";
                break;

            case "callback":
                var fromNl  = AiCallAssistent.Application.Helpers.NlTimeZone.ConvertFromUtc(now.AddHours(2));
                var untilNl = AiCallAssistent.Application.Helpers.NlTimeZone.ConvertFromUtc(now.AddHours(4));
                msg =
                    $"📞 Terugbelverzoek ontvangen, Testbeller!\n\n" +
                    $"Wij bellen u terug op {fromNl:dddd d MMMM} tussen {fromNl:HH:mm} en {untilNl:HH:mm}.\n\n" +
                    $"Staat u ergens anders voor open? Bel ons dan even.";
                messageType = "callback";
                break;

            default:
                return BadRequest(new { error = "type must be 'reminder', 'followup', or 'callback'" });
        }

        await whatsApp.SendForCompanyAsync(companyId, to, msg, messageType);

        return Ok(new { sent = true, to, type, companyId, companyName });
    }
}
