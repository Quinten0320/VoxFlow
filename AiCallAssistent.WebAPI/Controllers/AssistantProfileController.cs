using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/companies/{companyId:short}/profiles")]
public class AssistantProfileController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll(short companyId)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var profiles = await Db.AssistantProfiles
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.Name)
            .Select(p => MapToDto(p))
            .ToListAsync();

        return Ok(profiles);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(short companyId, int id)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var profile = await Db.AssistantProfiles
            .Where(p => p.Id == id && p.CompanyId == companyId)
            .Select(p => MapToDto(p))
            .FirstOrDefaultAsync();

        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpPost]
    public async Task<IActionResult> Create(short companyId, [FromBody] CreateAssistantProfileRequest request)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var profile = new AssistantProfile
        {
            CompanyId            = companyId,
            Name                 = request.Name,
            IsActive             = false,
            SystemPrompt         = request.SystemPrompt,
            GreetingMessage      = request.GreetingMessage,
            Language             = request.Language,
            CallMode             = request.CallMode,
            AfterHoursMode       = request.AfterHoursMode,
            BotActiveHoursEnabled = request.BotActiveHoursEnabled,
            CreatedAt            = now,
            UpdatedAt            = now
        };

        Db.AssistantProfiles.Add(profile);
        await Db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { companyId, id = profile.Id }, MapToDto(profile));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(short companyId, int id, [FromBody] UpdateAssistantProfileRequest request)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var rows = await Db.AssistantProfiles
            .Where(p => p.Id == id && p.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Name,                 request.Name)
                .SetProperty(p => p.SystemPrompt,         request.SystemPrompt)
                .SetProperty(p => p.GreetingMessage,      request.GreetingMessage)
                .SetProperty(p => p.Language,             request.Language)
                .SetProperty(p => p.CallMode,             request.CallMode)
                .SetProperty(p => p.AfterHoursMode,       request.AfterHoursMode)
                .SetProperty(p => p.BotActiveHoursEnabled, request.BotActiveHoursEnabled)
                .SetProperty(p => p.UpdatedAt,            DateTimeOffset.UtcNow));

        if (rows == 0) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(short companyId, int id)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var profile = await Db.AssistantProfiles
            .Where(p => p.Id == id && p.CompanyId == companyId)
            .FirstOrDefaultAsync();

        if (profile == null) return NotFound();
        if (profile.IsActive) return BadRequest(new { error = "Cannot delete the active profile. Deactivate it first." });

        Db.AssistantProfiles.Remove(profile);
        await Db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Activates the given profile and deactivates all others for this company.</summary>
    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(short companyId, int id)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        var profile = await Db.AssistantProfiles
            .Where(p => p.Id == id && p.CompanyId == companyId)
            .FirstOrDefaultAsync();

        if (profile == null) return NotFound();

        // Transactional swap — deactivate all, then activate this one.
        await using var tx = await Db.Database.BeginTransactionAsync();
        await Db.AssistantProfiles
            .Where(p => p.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));

        await Db.AssistantProfiles
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsActive, true)
                .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow));

        await tx.CommitAsync();
        return NoContent();
    }

    /// <summary>Deactivates all profiles for this company (fall back to base assistant_settings).</summary>
    [HttpPost("deactivate")]
    public async Task<IActionResult> DeactivateAll(short companyId)
    {
        var (authCompanyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;
        if (authCompanyId != companyId) return Forbid();

        await Db.AssistantProfiles
            .Where(p => p.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));

        return NoContent();
    }

    private static AssistantProfileDto MapToDto(AssistantProfile p) => new(
        p.Id, p.CompanyId, p.Name, p.IsActive,
        p.SystemPrompt, p.GreetingMessage,
        p.Language, p.CallMode, p.AfterHoursMode,
        p.BotActiveHoursEnabled, p.CreatedAt, p.UpdatedAt);
}
