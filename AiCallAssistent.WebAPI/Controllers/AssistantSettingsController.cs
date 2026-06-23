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
        var lockout = await RequireActiveSubscriptionAsync(companyId);
        if (lockout != null) return lockout;

        var settings = await Db.AssistantSettings
            .Where(s => s.CompanyId == companyId)
            .Select(s => new AssistantSettingsDto(
                s.VoiceId,
                s.Prompt,
                s.Language,
                s.GreetingsMessage,
                s.AppointmentsAutomaticallyToCalendar,
                s.AfterHoursMode,
                s.CallMode,
                s.BotActiveHoursEnabled,
                s.UpdatedAt,
                s.Tone,
                s.RoutingRules,
                s.AutoMessageConfig,
                s.NotificationConfig,
                s.AssistantName,
                s.AutoTimeGreeting,
                s.UseCallerName,
                s.TopicsYes,
                s.TopicsNo,
                s.FallbackBehavior,
                s.BehaviorInstructions))
            .FirstOrDefaultAsync();

        if (settings == null)
            return NotFound(new { message = "No assistant settings found for this company yet" });

        return Ok(settings);
    }

    /// <summary>Creates or updates assistant settings (upsert).</summary>
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
        settings.AppointmentsAutomaticallyToCalendar = request.AppointmentsAutomaticallyToCalendar;
        settings.AfterHoursMode = request.AfterHoursMode;
        settings.CallMode = request.CallMode;
        settings.BotActiveHoursEnabled = request.BotActiveHoursEnabled;
        settings.Tone = request.Tone;
        settings.RoutingRules = request.RoutingRules;
        settings.AutoMessageConfig = request.AutoMessageConfig;
        settings.NotificationConfig = request.NotificationConfig;
        settings.AssistantName        = request.AssistantName;
        settings.AutoTimeGreeting     = request.AutoTimeGreeting;
        settings.UseCallerName        = request.UseCallerName;
        settings.TopicsYes            = request.TopicsYes;
        settings.TopicsNo             = request.TopicsNo;
        settings.FallbackBehavior     = request.FallbackBehavior;
        settings.BehaviorInstructions = request.BehaviorInstructions;
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        await Db.SaveChangesAsync();

        return NoContent();
    }
}
