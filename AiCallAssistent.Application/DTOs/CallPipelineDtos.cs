namespace AiCallAssistent.Application.DTOs;

/// <summary>
/// Per-call context passed through the pipeline (controller, Gemini, dispatcher).
/// CallerNumber is the hook for future caller recognition: look up previous call_sessions
/// and pass the history as additional context before the conversation starts.
/// </summary>
public record CallDispatchContext(
    short CompanyId,
    string? CallerNumber,
    string? EscalationNumber);

/// <summary>
/// Company-specific settings loaded once per call from assistant_settings.
/// Null/empty values fall back to the built-in defaults.
/// </summary>
public record CompanyCallConfig(
    string? SystemPrompt,
    string Language,
    string? GreetingMessage);
