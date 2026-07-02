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
        const string? callerName = "Testbeller";

        switch (type.ToLowerInvariant())
        {
            case "reminder":
                await whatsApp.SendAppointmentReminderAsync(companyId, to, companyName, callerName, now.AddDays(1), "Testafspraak");
                break;

            case "day_reminder":
                await whatsApp.SendAppointmentDayReminderAsync(companyId, to, companyName, callerName, now.AddHours(2), "Testafspraak");
                break;

            case "confirmation":
                await whatsApp.SendAppointmentConfirmationAsync(companyId, to, companyName, callerName, now.AddDays(3), "Testafspraak");
                break;

            case "followup":
                await whatsApp.SendAppointmentFollowupAsync(companyId, to, companyName, callerName);
                break;

            case "callback":
                await whatsApp.SendCallbackConfirmationAsync(companyId, to, callerName);
                break;

            default:
                return BadRequest(new { error = "type must be 'confirmation', 'reminder', 'day_reminder', 'followup', or 'callback'" });
        }

        return Ok(new { sent = true, to, type, companyId, companyName });
    }
}
