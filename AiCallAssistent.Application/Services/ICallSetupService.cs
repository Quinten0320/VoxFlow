using System.Text.Json.Nodes;
using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

/// <summary>All data required to start answering a call — loaded once at the top of Answer().</summary>
public record CallSetupData(
    short CompanyId,
    string? EscalationNumber,
    CompanyFeatures Features,
    string Language,
    string? AfterHoursMode,
    bool IsAfterHours,
    string WelcomeText,
    string? Branch,
    JsonNode? AppointmentTypesNode,
    JsonNode? DepartmentsNode,
    string CallMode,
    bool BotActiveHoursEnabled,
    int? ActiveProfileId,
    IReadOnlyDictionary<string, string>? DepartmentPhones = null,
    string? SystemPrompt = null,
    string? WhatsAppFromNumber = null,
    string? AssistantName = null,
    string? Tone = null,
    bool AutoTimeGreeting = false,
    bool UseCallerName = false,
    string[]? TopicsYes = null,
    string[]? TopicsNo = null,
    string? FallbackBehavior = null,
    string? BehaviorInstructions = null,
    string? RoutingRulesJson = null);

/// <summary>All data required to process a recording turn — loaded once at the top of Recording().</summary>
public record CallRecordingContext(
    string Language,
    string? SystemPrompt,
    /// <summary>Non-null when after-hours mode is configured AND the company is currently closed.</summary>
    string? ActiveAfterHoursMode,
    string? Branch,
    string? EscalationNumber,
    IReadOnlyDictionary<string, string>? DepartmentPhones);

public interface ICallSetupService
{
    Task<CallSetupData> LoadAsync(string calledNumber, string callerNumber);
    Task<CallRecordingContext> LoadRecordingContextAsync(short companyId, string calledNumber);
}
