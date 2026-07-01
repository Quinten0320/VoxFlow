using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
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

        var isSubscriptionLocked = false;
        try
        {
            var pkg = await db.CompanyPackages
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .Select(p => new { p.SubscriptionStatus, p.CurrentPeriodEnd, p.AllowOverage })
                .FirstOrDefaultAsync();

            if (pkg != null)
            {
                isSubscriptionLocked = pkg.SubscriptionStatus switch
                {
                    "past_due" or "unpaid" => true,
                    "canceled" => pkg.CurrentPeriodEnd == null || pkg.CurrentPeriodEnd < DateTimeOffset.UtcNow,
                    _ => false,
                };

                // Minute-limit check: only blocks when AllowOverage = false and a limit is set.
                if (!isSubscriptionLocked && !pkg.AllowOverage && features.MaxCallMinutes.HasValue)
                {
                    // Count minutes used since the start of the current billing period.
                    var periodStart = pkg.CurrentPeriodEnd.HasValue
                        ? pkg.CurrentPeriodEnd.Value.AddMonths(-1)
                        : new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

                    var usedSeconds = await db.CallSessions
                        .Where(s => s.CompanyId == companyId
                                 && s.StartedAt >= periodStart
                                 && s.DurationSeconds != null)
                        .SumAsync(s => (long?)s.DurationSeconds ?? 0);

                    var usedMinutes = (int)(usedSeconds / 60);
                    if (usedMinutes >= features.MaxCallMinutes.Value)
                    {
                        logger.LogInformation(
                            "Call minute limit reached for company {CompanyId}: {Used}/{Limit} min — blocking call",
                            companyId, usedMinutes, features.MaxCallMinutes.Value);
                        isSubscriptionLocked = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check subscription status for company {CompanyId}", companyId);
        }

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

        var assistantName        = assistantSettings?.AssistantName;
        var tone                 = assistantSettings?.Tone;
        var autoTimeGreeting     = assistantSettings?.AutoTimeGreeting ?? false;
        var useCallerName        = assistantSettings?.UseCallerName ?? false;
        var topicsYes            = TryParseStringArray(assistantSettings?.TopicsYes);
        var topicsNo             = TryParseStringArray(assistantSettings?.TopicsNo);
        var fallbackBehavior     = assistantSettings?.FallbackBehavior;
        var behaviorInstructions = assistantSettings?.BehaviorInstructions;
        var routingRulesJson     = assistantSettings?.RoutingRules;
        var voiceKey             = assistantSettings?.VoiceKey;

        // Parse ForwardNumbers entries: handle "Buiten kantooruren" for escalation/after-hours,
        // and collect all other when-conditions to pass to the Gemini system prompt.
        string[]? forwardWhenConditions = null;
        if (assistantSettings?.ForwardNumbers is { Length: > 0 } fwdJson)
        {
            try
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(fwdJson);
                if (entries is { Length: > 0 })
                {
                    var nonAfterHours = new List<string>();
                    foreach (var entry in entries)
                    {
                        var when = entry.TryGetProperty("when", out var w) ? w.GetString() : null;
                        var number = entry.TryGetProperty("number", out var n) ? n.GetString() : null;
                        if (when?.Equals("Buiten kantooruren", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (string.IsNullOrEmpty(escalationNumber) && number is { Length: > 0 })
                            {
                                escalationNumber = number;
                                if (string.IsNullOrEmpty(afterHoursMode))
                                    afterHoursMode = Application.Constants.AfterHoursMode.TryHuman;
                            }
                        }
                        else if (when is { Length: > 0 })
                        {
                            // Use number as escalation fallback if no other escalation is configured
                            if (string.IsNullOrEmpty(escalationNumber) && number is { Length: > 0 })
                                escalationNumber = number;
                            nonAfterHours.Add(when);
                        }
                    }
                    if (nonAfterHours.Count > 0)
                        forwardWhenConditions = nonAfterHours.ToArray();
                }
            }
            catch { /* malformed JSON — ignore */ }
        }

        bool isAfterHours = false;
        if (features.AfterHoursMode && afterHoursMode is { Length: > 0 })
        {
            try { isAfterHours = !await openingHours.IsCompanyOpenAsync(companyId); }
            catch (Exception ex) { logger.LogWarning(ex, "After-hours check failed for company {CompanyId}", companyId); }
        }

        // Always use AssistantSettings greeting — no profile-level override since there is no UI to set one.
        var effectiveSettings = assistantSettings;
        string? greetingOverride  = null;
        var welcomeText = await BuildWelcomeTextAsync(
            companyId, isAfterHours, afterHoursMode, escalationNumber,
            effectiveSettings, greetingOverride, autoTimeGreeting, tone);

        string? branch = null;
        string? companyName = null;
        try
        {
            var company = await db.Companies
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .Select(c => new { c.Branch, c.CompanyName })
                .FirstOrDefaultAsync();
            branch = company?.Branch;
            companyName = company?.CompanyName;
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
            WhatsAppFromNumber:    assistantSettings?.WhatsAppPhoneNumber,
            AssistantName:         assistantName,
            Tone:                  tone,
            AutoTimeGreeting:      autoTimeGreeting,
            UseCallerName:         useCallerName,
            TopicsYes:             topicsYes,
            TopicsNo:              topicsNo,
            FallbackBehavior:      fallbackBehavior,
            BehaviorInstructions:  behaviorInstructions,
            RoutingRulesJson:        routingRulesJson,
            SubscriptionLocked:      isSubscriptionLocked,
            VoiceKey:                voiceKey,
            CompanyName:             companyName,
            ForwardWhenConditions:   forwardWhenConditions);
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
        string? greetingOverride = null, bool autoTimeGreeting = false, string? tone = null)
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

        // Tone-based hardcoded greeting takes priority over custom greeting.
        if (tone is { Length: > 0 })
        {
            var timeGreet = NlTimeZone.Now.Hour switch { < 12 => "Goedemorgen", < 18 => "Goedemiddag", _ => "Goedeavond" };
            return tone.ToLowerInvariant() switch
            {
                "vriendelijk"   => $"{timeGreet}, je spreekt met de virtuele assistent van {companyName}. Waarmee kan ik je helpen vandaag?",
                "professioneel" => $"{timeGreet}, u spreekt met de virtuele assistent van {companyName}. Hoe kan ik u van dienst zijn?",
                "neutraal"      => $"{timeGreet}, u spreekt met de virtuele assistent van {companyName}. Hoe kan ik u helpen?",
                "empathisch"    => $"{timeGreet}, u spreekt met de virtuele assistent van {companyName}. Waarmee kan ik u helpen?",
                _               => $"{timeGreet}, u spreekt met de virtuele assistent van {companyName}. Hoe kan ik u helpen?",
            };
        }

        // Fallback: custom greeting or default.
        if (greetingOverride is { Length: > 0 })
            return autoTimeGreeting ? PrependTimeGreeting(greetingOverride) : greetingOverride;

        if (assistantSettings?.GreetingsMessage is { Length: > 0 } greeting)
            return autoTimeGreeting ? PrependTimeGreeting(greeting) : greeting;

        var fallback = _assistant.WelcomeMessage.Replace("{company}", companyName);
        return autoTimeGreeting ? PrependTimeGreeting(fallback) : fallback;
    }

    private static string PrependTimeGreeting(string text)
    {
        var hour = NlTimeZone.Now.Hour;
        var prefix = hour < 12 ? "Goedemorgen" : hour < 18 ? "Goedemiddag" : "Goedeavond";
        // Avoid doubling if the greeting already starts with a time greeting
        if (text.StartsWith("Goedemorgen", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Goedemiddag",  StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Goedeavond",   StringComparison.OrdinalIgnoreCase))
            return text;
        return $"{prefix}! {text}";
    }

    private static string[]? TryParseStringArray(string? json)
    {
        if (json is not { Length: > 0 }) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json); }
        catch { return null; }
    }
}
