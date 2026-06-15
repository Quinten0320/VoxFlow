using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

public interface IGeminiService
{
    /// <summary>
    /// Runs one conversation turn. Pass null for config to use the built-in defaults.
    /// </summary>
    Task<GeminiTestResponse> RunConversationAsync(
        CallDispatchContext context,
        string userMessage,
        string? conversationId,
        CompanyCallConfig? config = null);

    /// <summary>
    /// Generates a short Dutch summary of a completed conversation.
    /// Returns an empty string if the conversation is not found or Gemini fails.
    /// </summary>
    Task<string> SummarizeConversationAsync(string conversationId);

    /// <summary>
    /// Classifies the caller based on the full conversation history.
    /// Returns one of: "lead", "verkoper", "informatie", "overig".
    /// Never throws — returns "overig" on any failure.
    /// </summary>
    Task<string> ClassifyCallerAsync(string conversationId);

    /// <summary>
    /// Generates 0–3 Dutch knowledge-base improvement suggestions from a call summary.
    /// Never throws — returns an empty list on any failure.
    /// </summary>
    Task<IReadOnlyList<string>> GenerateKnowledgeSuggestionsAsync(string summary, CancellationToken ct = default);
}
