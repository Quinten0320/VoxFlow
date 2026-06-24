using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/appointments")]
public class AppointmentController(AppDbContext db, IWhatsAppService whatsApp) : DashboardControllerBase(db)
{
    /// <summary>
    /// Returns appointments for the company.
    /// Optional query params: from (DateTimeOffset), to (DateTimeOffset).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var query = Db.Appointments
            .Where(a => a.CompanyId == companyId)
            .Join(Db.Employees,
                a => a.EmployeeId,
                e => e.EmployeeId,
                (a, e) => new { Appointment = a, EmployeeName = e.Name });

        if (from.HasValue)
            query = query.Where(x => x.Appointment.StartTime >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.Appointment.StartTime <= to.Value);

        var appointments = await query
            .OrderBy(x => x.Appointment.StartTime)
            .Select(x => new AppointmentDto(
                x.Appointment.AppointmentId,
                x.Appointment.EmployeeId,
                x.EmployeeName,
                x.Appointment.Type,
                x.Appointment.Description,
                x.Appointment.StartTime,
                x.Appointment.EndTime,
                x.Appointment.CreatedAt))
            .ToListAsync();

        return Ok(appointments);
    }

    /// <summary>Returns a single appointment by ID.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var appointment = await Db.Appointments
            .Where(a => a.AppointmentId == id && a.CompanyId == companyId)
            .Join(Db.Employees,
                a => a.EmployeeId,
                e => e.EmployeeId,
                (a, e) => new AppointmentDto(
                    a.AppointmentId,
                    a.EmployeeId,
                    e.Name,
                    a.Type,
                    a.Description,
                    a.StartTime,
                    a.EndTime,
                    a.CreatedAt))
            .FirstOrDefaultAsync();

        if (appointment == null)
            return NotFound();

        return Ok(appointment);
    }

    /// <summary>Creates a new appointment.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentDashboardRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var employeeExists = await Db.Employees
            .AnyAsync(e => e.EmployeeId == request.EmployeeId && e.CompanyId == companyId);
        if (!employeeExists)
            return BadRequest(new { error = "Employee not found or does not belong to this company" });

        var appointment = new Appointment
        {
            CompanyId = companyId,
            EmployeeId = request.EmployeeId,
            Type = request.Type,
            Description = request.Description,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Db.Appointments.Add(appointment);
        await Db.SaveChangesAsync();

        var employeeName = await Db.Employees
            .Where(e => e.EmployeeId == request.EmployeeId)
            .Select(e => e.Name)
            .FirstAsync();

        var dto = new AppointmentDto(
            appointment.AppointmentId,
            appointment.EmployeeId,
            employeeName,
            appointment.Type,
            appointment.Description,
            appointment.StartTime,
            appointment.EndTime,
            appointment.CreatedAt);

        return CreatedAtAction(nameof(GetById), new { id = appointment.AppointmentId }, dto);
    }

    /// <summary>Updates an existing appointment.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAppointmentDashboardRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var employeeExists = await Db.Employees
            .AnyAsync(e => e.EmployeeId == request.EmployeeId && e.CompanyId == companyId);
        if (!employeeExists)
            return BadRequest(new { error = "Employee not found or does not belong to this company" });

        var rows = await Db.Appointments
            .Where(a => a.AppointmentId == id && a.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.EmployeeId, request.EmployeeId)
                .SetProperty(a => a.Type, request.Type)
                .SetProperty(a => a.Description, request.Description)
                .SetProperty(a => a.StartTime, request.StartTime)
                .SetProperty(a => a.EndTime, request.EndTime));

        if (rows == 0)
            return NotFound();

        return NoContent();
    }

    /// <summary>Deletes an appointment.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.Appointments
            .Where(a => a.AppointmentId == id && a.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Sends a WhatsApp reminder to the given phone number for the specified appointment.
    /// Returns 204 on success, 404 if the appointment does not belong to this company.
    /// The message is sent best-effort — a 204 response means the send was attempted,
    /// not that it was delivered.
    /// </summary>
    [HttpPost("{id:long}/reminder")]
    public async Task<IActionResult> SendReminder(long id, [FromBody] SendReminderRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var appt = await Db.Appointments
            .Where(a => a.AppointmentId == id && a.CompanyId == companyId)
            .Join(Db.Employees,
                a => a.EmployeeId,
                e => e.EmployeeId,
                (a, e) => new { a.Type, a.StartTime, EmployeeName = e.Name })
            .FirstOrDefaultAsync();

        if (appt == null)
            return NotFound();

        var displayName = await Db.AppointmentTypes
            .Where(t => t.CompanyId == companyId && t.Name == appt.Type)
            .Select(t => t.DisplayName)
            .FirstOrDefaultAsync() ?? appt.Type;

        var companyName = await Db.Companies
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.CompanyName)
            .FirstOrDefaultAsync() ?? "ons bedrijf";

        var nl = AiCallAssistent.Application.Helpers.NlTimeZone.ConvertFromUtc(appt.StartTime);
        var msg =
            $"⏰ Herinnering: morgen heeft u een afspraak bij {companyName}!\n\n" +
            $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
            $"💇 {displayName}\n" +
            $"👤 {appt.EmployeeName}";

        await whatsApp.SendForCompanyAsync(companyId, request.PhoneNumber, msg, "reminder");

        return NoContent();
    }
}
