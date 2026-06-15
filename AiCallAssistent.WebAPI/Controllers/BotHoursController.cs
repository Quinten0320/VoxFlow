using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

/// <summary>Simpler bot-active-hours API for the dashboard (no companyId in URL).</summary>
[Route("api/bot-active-hours")]
public class BotHoursController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var items = await Db.BotActiveHours
            .Where(h => h.CompanyId == companyId && h.ProfileId == null)
            .OrderBy(h => h.DayOfWeek).ThenBy(h => h.OpenTime)
            .Select(h => new BotActiveHourDto(h.Id, h.DayOfWeek, h.OpenTime.ToString("HH:mm"), h.CloseTime.ToString("HH:mm"), h.ProfileId))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertBotActiveHourRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var hour = new BotActiveHour
        {
            CompanyId = companyId,
            DayOfWeek = request.DayOfWeek,
            OpenTime  = TimeOnly.Parse(request.OpenTime),
            CloseTime = TimeOnly.Parse(request.CloseTime),
            ProfileId = null
        };

        Db.BotActiveHours.Add(hour);
        await Db.SaveChangesAsync();

        return Ok(new BotActiveHourDto(hour.Id, hour.DayOfWeek, hour.OpenTime.ToString("HH:mm"), hour.CloseTime.ToString("HH:mm"), null));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.BotActiveHours
            .Where(h => h.Id == id && h.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0) return NotFound();
        return NoContent();
    }

    /// <summary>Replace all bot hours for the company (used by the save-all flow).</summary>
    [HttpPut]
    public async Task<IActionResult> ReplaceAll([FromBody] List<UpsertBotActiveHourRequest> requests)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        await Db.BotActiveHours
            .Where(h => h.CompanyId == companyId && h.ProfileId == null)
            .ExecuteDeleteAsync();

        var hours = requests.Select(r => new BotActiveHour
        {
            CompanyId = companyId,
            DayOfWeek = r.DayOfWeek,
            OpenTime  = TimeOnly.Parse(r.OpenTime),
            CloseTime = TimeOnly.Parse(r.CloseTime),
            ProfileId = null
        }).ToList();

        Db.BotActiveHours.AddRange(hours);
        await Db.SaveChangesAsync();

        return Ok(hours.Select(h => new BotActiveHourDto(h.Id, h.DayOfWeek, h.OpenTime.ToString("HH:mm"), h.CloseTime.ToString("HH:mm"), null)));
    }
}
