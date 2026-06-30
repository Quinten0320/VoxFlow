using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/dashboard")]
public class DashboardStatsController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>
    /// Returns all stats for the dashboard in a single request.
    /// Time boundaries are calculated in NL local time so "today" matches what the user sees.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var nowUtc = DateTimeOffset.UtcNow;
        var nowNl = NlTimeZone.Now;
        var todayNl = DateOnly.FromDateTime(nowNl.DateTime);

        var todayStartUtc = NlTimeZone.ToDateTimeOffset(todayNl, TimeOnly.MinValue).ToUniversalTime();
        var todayEndUtc   = NlTimeZone.ToDateTimeOffset(todayNl.AddDays(1), TimeOnly.MinValue).ToUniversalTime();

        var daysFromMonday = ((int)todayNl.DayOfWeek + 6) % 7;
        var weekStartUtc = NlTimeZone.ToDateTimeOffset(
            todayNl.AddDays(-daysFromMonday), TimeOnly.MinValue).ToUniversalTime();

        var monthStartUtc = NlTimeZone.ToDateTimeOffset(
            new DateOnly(todayNl.Year, todayNl.Month, 1), TimeOnly.MinValue).ToUniversalTime();

        var sevenDaysAgoUtc = NlTimeZone.ToDateTimeOffset(
            todayNl.AddDays(-6), TimeOnly.MinValue).ToUniversalTime();

        var callsToday = await Db.CallSessions
            .CountAsync(c => c.CompanyId == companyId
                && c.StartedAt >= todayStartUtc && c.StartedAt < todayEndUtc);

        var callsThisWeek = await Db.CallSessions
            .CountAsync(c => c.CompanyId == companyId && c.StartedAt >= weekStartUtc);

        var callsThisMonth = await Db.CallSessions
            .CountAsync(c => c.CompanyId == companyId && c.StartedAt >= monthStartUtc);

        // Fetch raw timestamps and group by NL date in memory to avoid timezone logic in SQL.
        var recentCallTimes = await Db.CallSessions
            .Where(c => c.CompanyId == companyId && c.StartedAt >= sevenDaysAgoUtc)
            .Select(c => c.StartedAt)
            .ToListAsync();

        var callsByDay = recentCallTimes
            .GroupBy(dt => DateOnly.FromDateTime(NlTimeZone.ConvertFromUtc(dt).DateTime))
            .ToDictionary(g => g.Key, g => g.Count());

        var last7Days = Enumerable.Range(0, 7)
            .Select(i => todayNl.AddDays(i - 6))
            .Select(d => new DayCountDto(d, callsByDay.TryGetValue(d, out var n) ? n : 0))
            .ToList();

        var appointmentsToday = await Db.Appointments
            .CountAsync(a => a.CompanyId == companyId
                && a.StartTime >= todayStartUtc && a.StartTime < todayEndUtc);

        var appointmentsThisWeek = await Db.Appointments
            .CountAsync(a => a.CompanyId == companyId && a.StartTime >= weekStartUtc);

        var upcomingRaw = await Db.Appointments
            .Where(a => a.CompanyId == companyId
                && a.StartTime >= nowUtc && a.StartTime < todayEndUtc)
            .Join(Db.Employees,
                a => a.EmployeeId,
                e => e.EmployeeId,
                (a, e) => new { a.StartTime, a.EndTime, a.Type, EmployeeName = e.Name })
            .OrderBy(x => x.StartTime)
            .Take(10)
            .ToListAsync();

        var typeDisplayNames = await Db.AppointmentTypes
            .Where(t => t.CompanyId == companyId)
            .Select(t => new { t.Name, t.DisplayName })
            .ToListAsync();

        var displayNameMap = typeDisplayNames.ToDictionary(t => t.Name, t => t.DisplayName);

        var upcomingToday = upcomingRaw
            .Select(x => new UpcomingAppointmentDto(
                NlTimeZone.ConvertFromUtc(x.StartTime),
                NlTimeZone.ConvertFromUtc(x.EndTime),
                x.Type,
                displayNameMap.TryGetValue(x.Type, out var dn) ? dn : x.Type,
                x.EmployeeName))
            .ToList();

        var typeCounts = await Db.Appointments
            .Where(a => a.CompanyId == companyId && a.StartTime >= monthStartUtc)
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var byTypeThisMonth = typeCounts
            .Select(tc => new TypeCountDto(
                tc.Type,
                displayNameMap.TryGetValue(tc.Type, out var dn) ? dn : tc.Type,
                tc.Count))
            .OrderByDescending(x => x.Count)
            .ToList();

        var openCallbacks = await Db.CallbackRequests
            .CountAsync(r => r.CompanyId == companyId && r.Status == "open");

        var yesterdayStartUtc = todayStartUtc.AddDays(-1);
        var missedToday = await Db.CallSessions
            .CountAsync(c => c.CompanyId == companyId
                && c.StartedAt >= todayStartUtc && c.StartedAt < todayEndUtc
                && c.Status == "no-answer");

        var avgDurationRaw = await Db.CallSessions
            .Where(c => c.CompanyId == companyId
                && c.StartedAt >= sevenDaysAgoUtc
                && c.DurationSeconds != null && c.DurationSeconds >= 15)
            .Select(c => (int?)c.DurationSeconds)
            .ToListAsync();
        var avgDurationSec = avgDurationRaw.Count > 0
            ? (int?)avgDurationRaw.Average(v => v!.Value)
            : null;

        var recentRaw = await Db.CallSessions
            .Where(c => c.CompanyId == companyId && c.Status == "completed")
            .OrderByDescending(c => c.StartedAt)
            .Take(5)
            .Select(c => new
            {
                c.CallSid, c.CallerNumber, c.CallerName,
                c.StartedAt, c.DurationSeconds, c.Summary, c.CallType, c.Status
            })
            .ToListAsync();

        var recentCalls = recentRaw
            .Select(c => new RecentCallDto(
                c.CallSid,
                c.CallerNumber,
                c.CallerName,
                c.StartedAt,
                c.DurationSeconds,
                c.Summary,
                c.CallType,
                c.Status))
            .ToList();

        return Ok(new DashboardStatsDto(
            GeneratedAt: nowUtc,
            Calls: new CallStatsDto(callsToday, callsThisWeek, callsThisMonth, last7Days,
                openCallbacks, missedToday, avgDurationSec),
            Appointments: new AppointmentStatsDto(
                appointmentsToday, appointmentsThisWeek, upcomingToday, byTypeThisMonth),
            RecentCalls: recentCalls));
    }
}
