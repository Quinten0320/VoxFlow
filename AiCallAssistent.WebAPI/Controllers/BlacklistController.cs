using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/blacklist")]
public class BlacklistController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var entries = await Db.CallBlacklist
            .Where(b => b.CompanyId == companyId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BlacklistEntryDto(
                b.BlacklistId,
                b.PhoneNumber,
                b.Reason,
                b.CreatedAt))
            .ToListAsync();

        return Ok(entries);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBlacklistEntryRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var entry = new CallBlacklist
        {
            CompanyId = companyId,
            PhoneNumber = request.PhoneNumber,
            Reason = request.Reason,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Db.CallBlacklist.Add(entry);
        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, $"Nummer geblokkeerd: {entry.PhoneNumber}");

        return Ok(new BlacklistEntryDto(
            entry.BlacklistId,
            entry.PhoneNumber,
            entry.Reason,
            entry.CreatedAt));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var entry = await Db.CallBlacklist
            .FirstOrDefaultAsync(b => b.BlacklistId == id && b.CompanyId == companyId);
        if (entry == null) return NotFound();

        Db.CallBlacklist.Remove(entry);
        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, $"Nummer gedeblokkeerd: {entry.PhoneNumber}");

        return NoContent();
    }
}
