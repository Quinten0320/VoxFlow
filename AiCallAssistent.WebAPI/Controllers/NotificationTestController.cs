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

        var fromNumber = await db.AssistantSettings
            .Where(s => s.CompanyId == companyId)
            .Select(s => s.WhatsAppPhoneNumber)
            .FirstOrDefaultAsync();

        var companyName = await db.Companies
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.CompanyName)
            .FirstOrDefaultAsync() ?? $"Bedrijf {companyId}";

        var now = DateTimeOffset.UtcNow;

        switch (type.ToLowerInvariant())
        {
            case "reminder":
                await whatsApp.SendAppointmentReminderAsync(
                    to,
                    displayName: "Testafspraak",
                    startTime: now.AddDays(1),
                    employeeName: "Test Medewerker",
                    companyName: companyName,
                    fromNumber: fromNumber);
                break;

            case "followup":
                await whatsApp.SendAppointmentFollowupAsync(
                    to,
                    displayName: "Testafspraak",
                    companyName: companyName,
                    fromNumber: fromNumber);
                break;

            case "callback":
                await whatsApp.SendCallbackConfirmationAsync(
                    to,
                    callerName: "Testbeller",
                    scheduledFrom: now.AddHours(2),
                    scheduledUntil: now.AddHours(4),
                    fromNumber: fromNumber);
                break;

            default:
                return BadRequest(new { error = "type must be 'reminder', 'followup', or 'callback'" });
        }

        return Ok(new
        {
            sent = true,
            to,
            type,
            from = fromNumber ?? "(global Twilio default)",
            companyId,
            companyName
        });
    }
}
