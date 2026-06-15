using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/companies/{companyId:int}/bot-active-hours")]
public class BotActiveHoursController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll(short companyId, [FromQuery] int? profileId = null)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        IQueryable<BotActiveHour> query = Db.BotActiveHours.Where(h => h.CompanyId == companyId);

        query = profileId.HasValue
            ? query.Where(h => h.ProfileId == profileId.Value)
            : query.Where(h => h.ProfileId == null);

        var items = await query
            .OrderBy(h => h.DayOfWeek).ThenBy(h => h.OpenTime)
            .Select(h => new BotActiveHourDto(h.Id, h.DayOfWeek, h.OpenTime.ToString("HH:mm"), h.CloseTime.ToString("HH:mm"), h.ProfileId))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(short companyId, [FromBody] UpsertBotActiveHourRequest request)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var hour = new BotActiveHour
        {
            CompanyId  = companyId,
            DayOfWeek  = request.DayOfWeek,
            OpenTime   = TimeOnly.Parse(request.OpenTime),
            CloseTime  = TimeOnly.Parse(request.CloseTime),
            ProfileId  = request.ProfileId
        };

        Db.BotActiveHours.Add(hour);
        await Db.SaveChangesAsync();

        var dto = new BotActiveHourDto(hour.Id, hour.DayOfWeek, hour.OpenTime.ToString("HH:mm"), hour.CloseTime.ToString("HH:mm"), hour.ProfileId);
        return CreatedAtAction(nameof(GetById), new { companyId, id = hour.Id }, dto);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(short companyId, int id)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var hour = await Db.BotActiveHours
            .Where(h => h.Id == id && h.CompanyId == companyId)
            .Select(h => new BotActiveHourDto(h.Id, h.DayOfWeek, h.OpenTime.ToString("HH:mm"), h.CloseTime.ToString("HH:mm"), h.ProfileId))
            .FirstOrDefaultAsync();

        if (hour == null) return NotFound();
        return Ok(hour);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(short companyId, int id, [FromBody] UpsertBotActiveHourRequest request)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var rows = await Db.BotActiveHours
            .Where(h => h.Id == id && h.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(h => h.DayOfWeek, request.DayOfWeek)
                .SetProperty(h => h.OpenTime,  TimeOnly.Parse(request.OpenTime))
                .SetProperty(h => h.CloseTime, TimeOnly.Parse(request.CloseTime)));

        if (rows == 0) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(short companyId, int id)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var rows = await Db.BotActiveHours
            .Where(h => h.Id == id && h.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0) return NotFound();
        return NoContent();
    }
}
