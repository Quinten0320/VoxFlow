using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/gdpr")]
public class GdprController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var company = await Db.Companies
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .Select(c => new { c.CompanyId, c.CompanyName, c.CompanyType, c.IsActive, c.CreatedAt })
            .FirstOrDefaultAsync();

        var employees = await Db.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.EmployeeId, e.Name, e.IsOwner, e.IsActive, e.CreatedAt })
            .ToListAsync();

        var appointmentTypes = await Db.AppointmentTypes
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .Select(t => new { t.AppointmentTypeId, t.Name, t.DisplayName, t.DurationMinutes, t.WaitTime, t.IsActive })
            .ToListAsync();

        var appointments = await Db.Appointments
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .Select(a => new
            {
                a.AppointmentId, a.EmployeeId, a.Type, a.Description,
                a.StartTime, a.EndTime, a.CallerPhoneNumber, a.CreatedAt
            })
            .ToListAsync();

        var callSessions = await Db.CallSessions
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => new
            {
                s.CallSid, s.PhoneNumber, s.CallerNumber, s.CallerName,
                s.StartedAt, s.EndedAt, s.DurationSeconds, s.Status,
                s.CallType, s.Summary, s.Transcript, s.CallerClassification,
                s.TranscriptionConfidence, s.CreatedAt
            })
            .ToListAsync();

        var callbackRequests = await Db.CallbackRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .Select(r => new
            {
                r.CallbackRequestId, r.CallerNumber, r.CallerName,
                r.Reason, r.ScheduledFrom, r.ScheduledUntil, r.Status, r.CreatedAt
            })
            .ToListAsync();

        var phoneNumbers = await Db.PhoneNumbers
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.PhoneNumberId, p.AiPhoneNumber, p.EscalationPhoneNumber, p.IsActive, p.CreatedAt })
            .ToListAsync();

        var departments = await Db.CompanyDepartments
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .Select(d => new { d.DepartmentId, d.Name, d.DisplayName, d.PhoneNumber, d.IsActive })
            .ToListAsync();

        var blacklist = await Db.CallBlacklist
            .AsNoTracking()
            .Where(b => b.CompanyId == companyId)
            .Select(b => new { b.BlacklistId, b.PhoneNumber, b.Reason, b.CreatedAt })
            .ToListAsync();

        var settings = await Db.AssistantSettings
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => new
            {
                s.Language, s.CallMode, s.AfterHoursMode, s.AssistantName,
                s.Tone, s.AutoTimeGreeting, s.UseCallerName, s.UpdatedAt
            })
            .FirstOrDefaultAsync();

        var pkg = await Db.CompanyPackages
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new
            {
                p.SubscriptionStatus, p.PlanName, p.BillingInterval,
                p.TrialEndsAt, p.CurrentPeriodEnd, p.UpdatedAt
            })
            .FirstOrDefaultAsync();

        var export = new
        {
            ExportedAt  = DateTimeOffset.UtcNow,
            CompanyId   = companyId,
            Company     = company,
            Settings    = settings,
            Package     = pkg,
            Employees   = employees,
            PhoneNumbers = phoneNumbers,
            Departments = departments,
            AppointmentTypes = appointmentTypes,
            Appointments     = appointments,
            CallSessions     = callSessions,
            CallbackRequests = callbackRequests,
            Blacklist        = blacklist,
        };

        return Ok(export);
    }
}
