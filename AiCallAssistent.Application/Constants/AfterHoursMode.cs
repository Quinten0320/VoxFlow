namespace AiCallAssistent.Application.Constants;

public static class AfterHoursMode
{
    /// <summary>Try escalation number first; fall back to callback recording if no answer.</summary>
    public const string TryHuman = "A";

    /// <summary>Go straight to callback recording — no transfer attempted.</summary>
    public const string CallbackOnly = "B";

    /// <summary>Bot picks up and operates fully (appointments + callbacks) — no restrictions.</summary>
    public const string FullService = "C";
}
