using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/opening-hours")]
public class OpeningHoursController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>
    /// Returns the full weekly schedule for the company (7 days, each with their time ranges).
    /// Days without a DB row are omitted — the frontend should treat them as closed.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var hours = await Db.CompanyOpeningHours
            .Where(h => h.CompanyId == companyId)
            .Include(h => h.TimeRanges)
            .OrderBy(h => h.DayOfWeek)
            .ToListAsync();

        var result = hours.Select(h => new OpeningHourDto(
            h.OpeningHourId,
            h.DayOfWeek,
            h.IsActive,
            h.TimeRanges
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder)
                .Select(r => new TimeRangeDto(
                    r.OpeningTimeRangeId,
                    r.StartTime.ToString("HH:mm"),
                    r.EndTime.ToString("HH:mm"),
                    r.SortOrder,
                    r.IsActive))
                .ToList()));

        return Ok(result);
    }

    /// <summary>
    /// Updates the opening hours for a single day.
    /// Replaces all existing time ranges with the ones in the request.
    /// dayOfWeek: 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri, 6=Sat, 7=Sun
    /// </summary>
    [HttpPut("{dayOfWeek:int}")]
    public async Task<IActionResult> Update(short dayOfWeek, [FromBody] UpdateOpeningHourRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        if (dayOfWeek < 1 || dayOfWeek > 7)
            return BadRequest(new { error = "dayOfWeek must be between 1 (Mon) and 7 (Sun)" });

        var hour = await Db.CompanyOpeningHours
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.DayOfWeek == dayOfWeek);

        if (hour == null)
        {
            hour = new CompanyOpeningHour
            {
                CompanyId = companyId,
                DayOfWeek = dayOfWeek,
                IsActive = request.IsActive
            };
            Db.CompanyOpeningHours.Add(hour);
            await Db.SaveChangesAsync();
        }
        else
        {
            hour.IsActive = request.IsActive;
            await Db.CompanyOpeningTimeRanges
                .Where(r => r.OpeningHourId == hour.OpeningHourId)
                .ExecuteDeleteAsync();
        }

        foreach (var r in request.TimeRanges)
        {
            if (!TimeOnly.TryParse(r.StartTime, out _) || !TimeOnly.TryParse(r.EndTime, out _))
                return BadRequest(new { error = $"Invalid time format '{r.StartTime}' or '{r.EndTime}'. Use HH:mm." });
        }

        var ranges = request.TimeRanges.Select(r => new CompanyOpeningTimeRange
        {
            OpeningHourId = hour.OpeningHourId,
            StartTime = TimeOnly.Parse(r.StartTime),
            EndTime = TimeOnly.Parse(r.EndTime),
            SortOrder = r.SortOrder,
            IsActive = true
        }).ToList();

        Db.CompanyOpeningTimeRanges.AddRange(ranges);
        await Db.SaveChangesAsync();

        return NoContent();
    }
}
