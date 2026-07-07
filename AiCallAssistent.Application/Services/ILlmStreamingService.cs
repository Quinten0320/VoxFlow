using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

public interface ILlmStreamingService
{
    /// <summary>
    /// Runs function-call iterations in batch mode, then streams the final text response via SSE.
    /// The caller must fully consume <see cref="LlmStreamResult.TextStream"/> for the
    /// conversation turn to be saved.
    /// </summary>
    Task<LlmStreamResult> RunConversationStreamingAsync(
        CallDispatchContext context,
        string userMessage,
        string conversationId,
        CompanyCallConfig? config,
        CancellationToken ct);

}

public record LlmStreamResult(
    bool Success,
    string? Error,
    IAsyncEnumerable<string> TextStream,
    string? EscalationNumber,
    string? FallbackNumber,
    string? AutoTransferNumber);
