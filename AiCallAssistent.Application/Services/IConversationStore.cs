using System.Text.Json.Nodes;

namespace AiCallAssistent.Application.Services;

public interface IConversationStore
{
    JsonArray Load(string conversationId);
    void Save(string conversationId, JsonArray contents);

    /// <summary>
    /// Seeds a new conversation with pre-existing turns.
    /// No-op if the conversation already exists (safe on retried Twilio webhooks).
    /// </summary>
    void Initialize(string conversationId, JsonArray initialTurns);

    /// <summary>Records the outcome type of a call (e.g. "Afspraak ingepland", "Terugbelverzoek", "Doorgeschakeld").</summary>
    void SetCallOutcome(string conversationId, string callType);

    /// <summary>Returns the recorded outcome type, or null if none was set.</summary>
    string? GetCallOutcome(string conversationId);
}
