using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DomainAssistantSettings = AiCallAssistent.Domain.Models.AssistantSettings;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/assistant-settings")]
public class AssistantSettingsController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>Returns assistant settings for the company.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var settings = await Db.AssistantSettings
            .Where(s => s.CompanyId == companyId)
            .Select(s => new AssistantSettingsDto(
                s.VoiceId,
                s.Prompt,
                s.Language,
                s.GreetingsMessage,
                s.UpdatedAt))
            .FirstOrDefaultAsync();

        if (settings == null)
            return NotFound(new { message = "No assistant settings found for this company yet" });

        return Ok(settings);
    }

    /// <summary>
    /// Creates or updates assistant settings for the company.
    /// Safe to call even if no row exists yet.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] UpdateAssistantSettingsRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var settings = await Db.AssistantSettings.FindAsync(companyId);

        if (settings == null)
        {
            settings = new DomainAssistantSettings { CompanyId = companyId };
            Db.AssistantSettings.Add(settings);
        }

        settings.VoiceId = request.VoiceId;
        settings.Prompt = request.Prompt;
        settings.Language = request.Language;
        settings.GreetingsMessage = request.GreetingsMessage;
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        await Db.SaveChangesAsync();

        return NoContent();
    }
}
