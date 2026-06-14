using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class CallSetupService(
    AppDbContext db,
    ICompanyPackageService packageService,
    IAppointmentService appointmentService,
    IOpeningHoursService openingHours,
    IOptions<Application.Configuration.AssistantSettings> assistantOptions,
    ILogger<CallSetupService> logger) : ICallSetupService
{
    private readonly Application.Configuration.AssistantSettings _assistant = assistantOptions.Value;

    public async Task<CallSetupData> LoadAsync(string calledNumber, string callerNumber)
    {
        short companyId = _assistant.DefaultCompanyId;
        string? escalationNumber = null;

        try
        {
            var phoneRow = await db.PhoneNumbers
                .AsNoTracking()
                .Where(p => p.AiPhoneNumber == calledNumber && p.IsActive)
                .Select(p => new { p.CompanyId, p.EscalationPhoneNumber })
                .FirstOrDefaultAsync();

            if (phoneRow != null)
            {
                companyId = phoneRow.CompanyId;
                escalationNumber = phoneRow.EscalationPhoneNumber;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not look up phone number {CalledNumber}", calledNumber);
        }

        var features = await packageService.GetFeaturesAsync(companyId);

        Domain.Models.AssistantSettings? assistantSettings = null;
        try
        {
            assistantSettings = await db.AssistantSettings
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load assistant settings for company {CompanyId}", companyId);
        }

        // Load active profile and overlay its values on top of assistant settings.
        AssistantProfile? activeProfile = null;
        try
        {
            activeProfile = await db.AssistantProfiles
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.IsActive)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load active assistant profile for company {CompanyId}", companyId);
        }

        var language         = activeProfile?.Language
                            ?? (assistantSettings?.Language is { Length: > 0 } l ? l : "nl");
        var afterHoursMode   = activeProfile?.AfterHoursMode ?? assistantSettings?.AfterHoursMode;
        var callMode         = activeProfile?.CallMode
                            ?? (assistantSettings?.CallMode is { Length: > 0 } cm ? cm : CallMode.FirstLine);
        var botActiveHours   = activeProfile?.BotActiveHoursEnabled
                            ?? assistantSettings?.BotActiveHoursEnabled
                            ?? false;
        int? activeProfileId = activeProfile?.Id;
        var systemPrompt     = activeProfile?.SystemPrompt is { Length: > 0 } sp ? sp
                            : assistantSettings?.Prompt is { Length: > 0 } p2 ? p2
                            : null;

        bool isAfterHours = false;
        if (features.AfterHoursMode && afterHoursMode is { Length: > 0 })
        {
            try { isAfterHours = !await openingHours.IsCompanyOpenAsync(companyId); }
            catch (Exception ex) { logger.LogWarning(ex, "After-hours check failed for company {CompanyId}", companyId); }
        }

        // Build the welcome text using profile greeting if available.
        var effectiveSettings = assistantSettings;
        var greetingOverride  = activeProfile?.GreetingMessage;
        var welcomeText = await BuildWelcomeTextAsync(
            companyId, isAfterHours, afterHoursMode, escalationNumber,
            effectiveSettings, greetingOverride);

        string? branch = null;
        try
        {
            branch = await db.Companies
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.Branch)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load company branch for company {CompanyId}", companyId);
        }

        JsonNode? appointmentTypesNode = null;
        try
        {
            var types = await appointmentService.GetAppointmentTypesAsync(companyId);
            appointmentTypesNode = JsonSerializer.SerializeToNode(types);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not pre-fetch appointment types for company {CompanyId}", companyId);
        }

        JsonNode? departmentsNode = null;
        Dictionary<string, string>? departmentPhones = null;
        if (features.DepartmentRouting)
        {
            try
            {
                var depts = await db.CompanyDepartments
                    .AsNoTracking()
                    .Where(d => d.CompanyId == companyId && d.IsActive)
                    .Select(d => new { d.Name, d.DisplayName, d.PhoneNumber })
                    .ToListAsync();

                if (depts.Count > 0)
                {
                    departmentPhones = depts.ToDictionary(d => d.Name, d => d.PhoneNumber);
                    departmentsNode = JsonSerializer.SerializeToNode(
                        depts.Select(d => new { d.Name, d.DisplayName }).ToList());
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not pre-fetch departments for company {CompanyId}", companyId);
            }
        }

        return new CallSetupData(
            companyId, escalationNumber, features, language,
            afterHoursMode, isAfterHours, welcomeText, branch,
            appointmentTypesNode, departmentsNode,
            callMode, botActiveHours, activeProfileId, departmentPhones, systemPrompt,
            WhatsAppFromNumber: assistantSettings?.WhatsAppPhoneNumber);
    }

    public async Task<CallRecordingContext> LoadRecordingContextAsync(short companyId, string calledNumber)
    {
        string language = "nl";
        string? systemPrompt = null;
        string? configuredAfterHoursMode = null;
        string? branch = null;
        string? escalationNumber = null;
        Dictionary<string, string>? departmentPhones = null;

        try
        {
            var settings = await db.AssistantSettings
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .FirstOrDefaultAsync();

            if (settings != null)
            {
                if (settings.Language is { Length: > 0 } l) language = l;
                if (settings.Prompt is { Length: > 0 } p) systemPrompt = p;
                if (settings.AfterHoursMode is { Length: > 0 } m) configuredAfterHoursMode = m;
            }

            // Active profile overrides base settings.
            var profile = await db.AssistantProfiles
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.IsActive)
                .FirstOrDefaultAsync();

            if (profile != null)
            {
                if (profile.Language is { Length: > 0 } pl) language = pl;
                if (profile.SystemPrompt is { Length: > 0 } pp) systemPrompt = pp;
                if (profile.AfterHoursMode is { Length: > 0 } pm) configuredAfterHoursMode = pm;
            }

            branch = await db.Companies
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.Branch)
                .FirstOrDefaultAsync();

            escalationNumber = await db.PhoneNumbers
                .AsNoTracking()
                .Where(p => p.AiPhoneNumber == calledNumber && p.IsActive)
                .Select(p => p.EscalationPhoneNumber)
                .FirstOrDefaultAsync();

            var depts = await db.CompanyDepartments
                .AsNoTracking()
                .Where(d => d.CompanyId == companyId && d.IsActive)
                .Select(d => new { d.Name, d.PhoneNumber })
                .ToListAsync();

            if (depts.Count > 0)
                departmentPhones = depts.ToDictionary(d => d.Name, d => d.PhoneNumber);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load recording context for company {CompanyId}", companyId);
        }

        // Resolve whether the company is currently closed so the caller (Recording endpoint)
        // doesn't need to repeat the opening-hours check.
        string? activeAfterHoursMode = null;
        if (configuredAfterHoursMode is { Length: > 0 })
        {
            try
            {
                var isOpen = await openingHours.IsCompanyOpenAsync(companyId);
                if (!isOpen) activeAfterHoursMode = configuredAfterHoursMode;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "After-hours check failed in recording context for company {CompanyId}", companyId);
            }
        }

        return new CallRecordingContext(language, systemPrompt, activeAfterHoursMode, branch, escalationNumber, departmentPhones);
    }

    private async Task<string> BuildWelcomeTextAsync(
        short companyId, bool isAfterHours, string? afterHoursMode,
        string? escalationNumber, Domain.Models.AssistantSettings? assistantSettings,
        string? greetingOverride = null)
    {
        if (isAfterHours)
        {
            return afterHoursMode switch
            {
                AfterHoursMode.TryHuman when escalationNumber is { Length: > 0 }
                    => "Goed dat u belt! We zijn momenteel gesloten. Ik probeer u door te verbinden met een van onze medewerkers.",
                AfterHoursMode.FullService
                    => "Goed dat u belt! We zijn momenteel gesloten, maar ik kan u gewoon verder helpen. Ik kan een afspraak voor u inplannen of een terugbelverzoek vastleggen.",
                _   => "Goed dat u belt! We zijn momenteel gesloten. U kunt mij een terugbelverzoek achterlaten, dan nemen wij zo snel mogelijk contact met u op."
            };
        }

        // Profile greeting takes precedence over base assistant settings greeting.
        if (greetingOverride is { Length: > 0 })
            return greetingOverride;

        if (assistantSettings?.GreetingsMessage is { Length: > 0 } greeting)
            return greeting;

        var companyName = "ons bedrijf";
        try
        {
            var name = await db.Companies
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.CompanyName)
                .FirstOrDefaultAsync();
            if (name is { Length: > 0 }) companyName = name;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load company name for company {CompanyId}", companyId);
        }

        return _assistant.WelcomeMessage.Replace("{company}", companyName);
    }
}
