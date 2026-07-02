using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

public record AgendaEventDto(
    string Id,
    string Source,
    string Title,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? EmployeeName,
    string? CallerPhone);

[Route("api/agenda")]
public class AgendaController(AppDbContext db, IOutlookCalendarService outlookCalendar)
    : DashboardControllerBase(db)
{
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming([FromQuery] int days = 60)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var from = DateTimeOffset.UtcNow;
        var to = from.AddDays(Math.Clamp(days, 1, 180));

        var dbAppointments = await Db.Appointments
            .Where(a => a.CompanyId == companyId && a.StartTime >= from && a.StartTime <= to)
            .Join(Db.Employees,
                a => a.EmployeeId,
                e => e.EmployeeId,
                (a, e) => new { Appointment = a, EmployeeName = e.Name })
            .OrderBy(x => x.Appointment.StartTime)
            .ToListAsync();

        List<AgendaEventDto> outlookEvents = [];
        string? outlookError = null;
        try
        {
            var graphEvents = await outlookCalendar.GetEventsAsync(companyId, from, to);
            outlookEvents = graphEvents
                .Select(e => new AgendaEventDto(
                    $"outlook-{e.Start.Ticks}",
                    "outlook",
                    e.Subject,
                    e.BodyPreview,
                    e.Start,
                    e.End,
                    null,
                    null))
                .ToList();
        }
        catch (Exception ex)
        {
            outlookError = ex.Message;
        }

        var dbEvents = dbAppointments.Select(x => new AgendaEventDto(
            $"db-{x.Appointment.AppointmentId}",
            "db",
            x.Appointment.Type,
            x.Appointment.Description,
            x.Appointment.StartTime,
            x.Appointment.EndTime,
            x.EmployeeName,
            x.Appointment.CallerPhoneNumber));

        var merged = dbEvents
            .Concat(outlookEvents)
            .OrderBy(e => e.Start)
            .ToList();

        return Ok(new { items = merged, outlookError });
    }
}
