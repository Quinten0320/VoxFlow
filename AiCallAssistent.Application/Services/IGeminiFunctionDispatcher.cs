using System.Text.Json.Nodes;
using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

public interface IGeminiFunctionDispatcher
{
    Task<object> DispatchAsync(CallDispatchContext context, string functionName, JsonNode? args);
    JsonObject GetToolDeclarations();
}
