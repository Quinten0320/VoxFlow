using System.Text.Json.Nodes;
using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

public interface IGeminiFunctionDispatcher
{
    Task<object> DispatchAsync(
        CallDispatchContext context,
        string functionName,
        JsonNode? args,
        IConversationStore? store = null,
        string? conversationId = null);

    JsonObject GetToolDeclarations(string? branch = null, CompanyFeatures? features = null, string[]? forwardWhenConditions = null);
}
