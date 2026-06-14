namespace AiCallAssistent.Application.DTOs;

/// <summary>
/// Per-company feature flags and usage limits loaded from company_package.
/// When no row exists all features are enabled and limits are unlimited — use <see cref="Default"/>.
/// </summary>
public record CompanyFeatures(
    int? MaxCallMinutes,
    int? MaxWhatsAppPerMonth,
    bool Blacklist,
    bool CallbackRequests,
    bool WhatsAppConfirmation,
    bool WhatsAppReminders,
    bool DepartmentRouting,
    bool TransferToHuman,
    bool AfterHoursMode,
    bool BranchTools)
{
    /// <summary>All features on, unlimited usage — used when no package row is configured.</summary>
    public static CompanyFeatures Default { get; } = new(
        MaxCallMinutes: null,
        MaxWhatsAppPerMonth: null,
        Blacklist: true,
        CallbackRequests: true,
        WhatsAppConfirmation: true,
        WhatsAppReminders: true,
        DepartmentRouting: true,
        TransferToHuman: true,
        AfterHoursMode: true,
        BranchTools: true);
}

/// <summary>
/// Per-call context passed through the pipeline (controller, Gemini, dispatcher).
/// </summary>
public record CallDispatchContext(
    short CompanyId,
    string? CallerNumber,
    string? EscalationNumber,
    IReadOnlyDictionary<string, string>? DepartmentPhones = null,
    string? Branch = null,
    CompanyFeatures? Features = null);

/// <summary>
/// Company-specific settings loaded once per call from assistant_settings.
/// Null/empty values fall back to the built-in defaults.
/// </summary>
public record CompanyCallConfig(
    string? SystemPrompt,
    string Language,
    string? GreetingMessage,
    string? AfterHoursMode = null,
    bool IsWhatsApp = false);
