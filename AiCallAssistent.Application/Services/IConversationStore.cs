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
}
