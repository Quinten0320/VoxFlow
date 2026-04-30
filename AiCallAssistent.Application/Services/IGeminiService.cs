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
}
