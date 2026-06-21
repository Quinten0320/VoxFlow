using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/dashboard")]
public class DashboardReportingController(AppDbContext db) : DashboardControllerBase(db)
{
    private static readonly string[] DayAbbr = ["Ma", "Di", "Wo", "Do", "Vr", "Za", "Zo"];

    /// <summary>
    /// Returns reporting data for the dashboard.
    /// range: vandaag | week | maand (default: week)
    /// </summary>
    [HttpGet("reporting")]
    public async Task<IActionResult> GetReporting([FromQuery] string range = "week")
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var nowNl  = NlTimeZone.Now;
        var todayNl = DateOnly.FromDateTime(nowNl.DateTime);

        DateTimeOffset startUtc = range switch
        {
            "vandaag" => NlTimeZone.ToDateTimeOffset(todayNl, TimeOnly.MinValue).ToUniversalTime(),
            "maand"   => NlTimeZone.ToDateTimeOffset(new DateOnly(todayNl.Year, todayNl.Month, 1), TimeOnly.MinValue).ToUniversalTime(),
            _         => NlTimeZone.ToDateTimeOffset(todayNl.AddDays(-6), TimeOnly.MinValue).ToUniversalTime()
        };

        // Pull raw sessions in window
        var sessions = await Db.CallSessions
            .Where(c => c.CompanyId == companyId && c.StartedAt >= startUtc)
            .Select(c => new
            {
                c.StartedAt,
                c.CallerNumber,
                CallType = c.CallType ?? "Info",
                c.DurationSeconds
            })
            .ToListAsync();

        // CallsPerDay — group by NL day-of-week (Mon=0..Sun=6)
        var callsByDow = sessions
            .GroupBy(s => (int)NlTimeZone.ConvertFromUtc(s.StartedAt).DayOfWeek)
            .ToDictionary(g => (g.Key + 6) % 7, g => g.Count()); // convert Sunday=0 to index 6

        var callsPerDay = DayAbbr
            .Select((abbr, i) => new DayCallCountDto(abbr, callsByDow.TryGetValue(i, out var n) ? n : 0))
            .ToList();

        // CallTypeDistribution
        var callTypeDistribution = sessions
            .GroupBy(s => s.CallType)
            .Select(g => new CallTypeCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Value)
            .ToList();

        // PeakHeatmap — hours 8-19, by NL day
        var peakHeatmap = DayAbbr.Select((abbr, dowIdx) =>
        {
            var cells = Enumerable.Range(8, 12).Select(hour =>
            {
                var count = sessions.Count(s =>
                {
                    var nl = NlTimeZone.ConvertFromUtc(s.StartedAt);
                    return (((int)nl.DayOfWeek + 6) % 7) == dowIdx && nl.Hour == hour;
                });
                return new PeakHeatmapCellDto(hour, count);
            }).ToList();
            return new PeakHeatmapDayDto(abbr, cells);
        }).ToList();

        // AvgDurationSec
        var durationValues = sessions
            .Where(s => s.DurationSeconds is >= 15)
            .Select(s => s.DurationSeconds!.Value)
            .ToList();
        int? avgDurationSec = durationValues.Count > 0 ? (int)durationValues.Average() : null;

        // FirstCallResolutionPct: callers who did NOT call back within 24h
        var total = sessions.Count;
        int firstCallResolutionPct = 100;
        if (total > 0)
        {
            var repeatCallers = sessions
                .GroupBy(s => s.CallerNumber)
                .Count(g => g.Count() > 1 && (g.Max(s => s.StartedAt) - g.Min(s => s.StartedAt)).TotalHours <= 24);
            firstCallResolutionPct = (int)Math.Round((double)(total - repeatCallers) / total * 100);
        }

        // Callback open/handled
        var callbackOpen = await Db.CallbackRequests
            .CountAsync(r => r.CompanyId == companyId && r.Status == "open");
        var callbackHandled = await Db.CallbackRequests
            .CountAsync(r => r.CompanyId == companyId && r.Status == "handled");

        return Ok(new ReportingDto(
            callsPerDay,
            callTypeDistribution,
            peakHeatmap,
            avgDurationSec,
            firstCallResolutionPct,
            callbackOpen,
            callbackHandled));
    }
}
