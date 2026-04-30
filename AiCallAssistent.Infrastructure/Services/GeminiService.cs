using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _http;
    private readonly IGeminiFunctionDispatcher _dispatcher;
    private readonly IConversationStore _conversations;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiService> _logger;

    // Tool declarations don't change at runtime, so serialize them once at startup.
    private readonly string _toolsJson;

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const int MaxIterations = 8;

    public GeminiService(
        HttpClient http,
        IGeminiFunctionDispatcher dispatcher,
        IConversationStore conversations,
        IOptions<GeminiSettings> settings,
        ILogger<GeminiService> logger)
    {
        _http = http;
        _dispatcher = dispatcher;
        _conversations = conversations;
        _settings = settings.Value;
        _logger = logger;
        _toolsJson = new JsonArray { dispatcher.GetToolDeclarations() }.ToJsonString();
    }

    public async Task<GeminiTestResponse> RunConversationAsync(
        CallDispatchContext context,
        string userMessage,
        string? conversationId,
        CompanyCallConfig? config = null)
    {
        conversationId ??= Guid.NewGuid().ToString();
        var logs = new List<GeminiFunctionCallLog>();
        string? pendingEscalation = null;

        var contents = _conversations.Load(conversationId);
        contents.Add(UserTurn(userMessage));

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var contentsJson = contents.ToJsonString();
            var response = await CallGeminiAsync(contentsJson, context, config);

            if (!response.Success)
                return ErrorResponse(response.Error!, logs, iteration + 1, conversationId, contents.Count);

            var part = response.Part!;

            if (part["text"] is JsonNode textNode)
            {
                var text = textNode.GetValue<string>();
                contents.Add(ModelTextTurn(text));
                _conversations.Save(conversationId, contents);

                return new GeminiTestResponse
                {
                    Success = true,
                    FinalResponse = text,
                    FunctionCalls = logs,
                    Iterations = iteration + 1,
                    ConversationId = conversationId,
                    TotalMessages = contents.Count,
                    EscalationNumber = pendingEscalation
                };
            }

            if (part["functionCall"] is JsonObject functionCall)
            {
                var funcName = functionCall["name"]!.GetValue<string>();
                var funcArgs = functionCall["args"];

                contents.Add(ModelFunctionCallTurn(functionCall));

                var log = new GeminiFunctionCallLog
                {
                    FunctionName = funcName,
                    Args = funcArgs is not null ? JsonSerializer.Deserialize<object>(funcArgs.ToJsonString()) : null
                };

                try
                {
                    var result = await _dispatcher.DispatchAsync(context, funcName, funcArgs);
                    log.Result = result;
                    log.Success = true;
                    contents.Add(FunctionResponseTurn(funcName, new JsonObject { ["result"] = JsonSerializer.SerializeToNode(result) }));

                    if (funcName == "transfer_to_human" && context.EscalationNumber is { Length: > 0 })
                        pendingEscalation = context.EscalationNumber;
                }
                catch (Exception ex)
                {
                    log.Success = false;
                    log.Error = ex.Message;
                    contents.Add(FunctionResponseTurn(funcName, new JsonObject { ["error"] = ex.Message }));
                }

                logs.Add(log);
                continue;
            }

            return ErrorResponse($"Unexpected response part: {part.ToJsonString()}", logs, iteration + 1, conversationId, contents.Count);
        }

        return ErrorResponse("Max iterations reached without a text response from Gemini.", logs, MaxIterations, conversationId, contents.Count);
    }

    private async Task<GeminiCallResult> CallGeminiAsync(
        string contentsJson, CallDispatchContext context, CompanyCallConfig? config)
    {
        var requestBytes = BuildRequestBytes(contentsJson, context, config);
        var url = $"{BaseUrl}/{_settings.Model}:generateContent?key={_settings.ApiKey}";

        using var content = new ByteArrayContent(requestBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        var httpResponse = await _http.PostAsync(url, content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
            return GeminiCallResult.Fail($"Gemini API error {(int)httpResponse.StatusCode}: {responseJson}");

        var root = JsonNode.Parse(responseJson);
        var candidates = root?["candidates"]?.AsArray();

        if (candidates is null || candidates.Count == 0)
            return GeminiCallResult.Fail("Gemini returned no candidates.");

        var firstPart = candidates[0]?["content"]?["parts"]?[0];
        if (firstPart is null)
        {
            _logger.LogError("Gemini response had no parts. Full response: {Response}", responseJson);
            return GeminiCallResult.Fail("Gemini response had no parts.");
        }

        return GeminiCallResult.Ok(firstPart);
    }

    private byte[] BuildRequestBytes(string contentsJson, CallDispatchContext context, CompanyCallConfig? config)
    {
        var nowNl = NlTimeZone.Now;
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 8192);
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();

        writer.WritePropertyName("system_instruction");
        writer.WriteStartObject();
        writer.WritePropertyName("parts");
        writer.WriteStartArray();
        writer.WriteStartObject();

        var systemPrompt = config?.SystemPrompt is { Length: > 0 } p ? p : null;
        writer.WriteString("text", systemPrompt ??
            $"""
            Je bent een vriendelijke AI-telefoonassistent die afspraken boekt voor bedrijf met ID {context.CompanyId}.
            Help de beller een afspraak te plannen via de beschikbare tools.
            Bevestig altijd de afspraakdetails voordat je daadwerkelijk een boeking maakt.
            Spreek altijd en uitsluitend Nederlands — gebruik nooit een andere taal.
            Praat natuurlijk en beknopt, alsof je aan de telefoon bent.
            Alle tijden zijn in Nederlandse lokale tijd (Europe/Amsterdam).
            Vandaag is {nowNl:dddd, d MMMM yyyy} en de huidige tijd is {nowNl:HH:mm}.
            Los relatieve datums zoals "morgen", "volgende maandag" of "aanstaande dinsdag" op met de datum van vandaag.
            Vraag de beller nooit om een datum die je zelf kunt berekenen.

            REGELS VOOR TOOLS:
            - Zeg nooit "momentje" of "ik zoek het even op" voordat je een tool aanroept. Roep de tool meteen aan en geef daarna pas antwoord.
            - Vraagt de beller welke afspraken beschikbaar zijn? Roep ONMIDDELLIJK get_appointment_types aan. Reageer niet eerst met tekst.
            - Wil de beller boeken of vraagt hij naar beschikbaarheid? Roep ONMIDDELLIJK get_soonest_available of check_availability aan.
            - Haal altijd actuele data op via de tools voordat je antwoord geeft over diensten, beschikbaarheid of tijden.
            """);

        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("contents");
        writer.WriteRawValue(contentsJson);

        writer.WritePropertyName("tools");
        writer.WriteRawValue(_toolsJson);

        writer.WritePropertyName("tool_config");
        writer.WriteStartObject();
        writer.WritePropertyName("function_calling_config");
        writer.WriteStartObject();
        writer.WriteString("mode", "AUTO");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    public async Task<string> SummarizeConversationAsync(string conversationId)
    {
        try
        {
            var contents = _conversations.Load(conversationId);
            if (contents.Count == 0)
            {
                _logger.LogWarning("SummarizeConversation: no history found for {ConversationId}", conversationId);
                return string.Empty;
            }

            var requestBytes = BuildSummaryRequestBytes(contents.ToJsonString());
            var url = $"{BaseUrl}/{_settings.Model}:generateContent?key={_settings.ApiKey}";

            using var content = new ByteArrayContent(requestBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            var httpResponse = await _http.PostAsync(url, content);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Gemini summarization failed for {ConversationId} — HTTP {StatusCode}: {Body}",
                    conversationId, (int)httpResponse.StatusCode, errorBody);
                return string.Empty;
            }

            var responseJson = await httpResponse.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(responseJson);
            var summary = root?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>()
                          ?? string.Empty;

            if (string.IsNullOrWhiteSpace(summary))
                _logger.LogWarning("Gemini returned an empty summary for {ConversationId}", conversationId);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SummarizeConversation threw for {ConversationId}", conversationId);
            return string.Empty;
        }
    }

    private static byte[] BuildSummaryRequestBytes(string contentsJson)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 4096);
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();

        writer.WritePropertyName("system_instruction");
        writer.WriteStartObject();
        writer.WritePropertyName("parts");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("text",
            """
            Maak een beknopte samenvatting (maximaal 3 zinnen) van het volgende telefoongesprek.
            Vermeld: het doel van het gesprek, eventuele geboekte afspraak (type, datum en tijd), en het eindresultaat.
            Schrijf in het Nederlands.
            """);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("contents");
        writer.WriteRawValue(contentsJson);

        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    private static JsonObject UserTurn(string text) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
    };

    private static JsonObject ModelTextTurn(string text) => new()
    {
        ["role"] = "model",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
    };

    private static JsonObject ModelFunctionCallTurn(JsonObject functionCall) => new()
    {
        ["role"] = "model",
        // functionCall is a child node of the parsed response — DeepClone before reparenting
        ["parts"] = new JsonArray { new JsonObject { ["functionCall"] = functionCall.DeepClone() } }
    };

    private static JsonObject FunctionResponseTurn(string name, JsonObject response) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray
        {
            new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = name,
                    ["response"] = response
                }
            }
        }
    };

    private static GeminiTestResponse ErrorResponse(string error, List<GeminiFunctionCallLog> logs,
        int iterations, string conversationId, int totalMessages) => new()
    {
        Success = false,
        Error = error,
        FunctionCalls = logs,
        Iterations = iterations,
        ConversationId = conversationId,
        TotalMessages = totalMessages
    };

    private record GeminiCallResult(bool Success, string? Error, JsonNode? Part)
    {
        public static GeminiCallResult Ok(JsonNode part) => new(true, null, part);
        public static GeminiCallResult Fail(string error) => new(false, error, null);
    }
}
