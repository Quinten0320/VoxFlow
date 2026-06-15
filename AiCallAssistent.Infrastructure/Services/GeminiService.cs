using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _http;
    private readonly IGeminiFunctionDispatcher _dispatcher;
    private readonly IConversationStore _conversations;
    private readonly IVertexAiTokenProvider _tokenProvider;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiService> _logger;

    private const int MaxIterations = 8;

    private string EndpointBase =>
        $"https://{_settings.Location}-aiplatform.googleapis.com/v1" +
        $"/projects/{_settings.ProjectId}/locations/{_settings.Location}" +
        $"/publishers/google/models";

    public GeminiService(
        HttpClient http,
        IGeminiFunctionDispatcher dispatcher,
        IConversationStore conversations,
        IVertexAiTokenProvider tokenProvider,
        IOptions<GeminiSettings> settings,
        ILogger<GeminiService> logger)
    {
        _http = http;
        _dispatcher = dispatcher;
        _conversations = conversations;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
        _logger = logger;
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
        string? pendingFallback = null;
        string? pendingAutoTransfer = null;

        var contents = _conversations.Load(conversationId);
        contents.Add(GeminiRequestBuilder.UserTurn(userMessage));

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
                contents.Add(GeminiRequestBuilder.ModelTextTurn(text));
                _conversations.Save(conversationId, contents);

                return new GeminiTestResponse
                {
                    Success = true,
                    FinalResponse = text,
                    FunctionCalls = logs,
                    Iterations = iteration + 1,
                    ConversationId = conversationId,
                    TotalMessages = contents.Count,
                    EscalationNumber = pendingEscalation,
                    FallbackNumber = pendingFallback,
                    AutoTransferNumber = pendingAutoTransfer
                };
            }

            if (part["functionCall"] is JsonObject functionCall)
            {
                var funcName = functionCall["name"]!.GetValue<string>();
                var funcArgs = functionCall["args"];

                contents.Add(GeminiRequestBuilder.ModelFunctionCallTurn(functionCall));

                var log = new GeminiFunctionCallLog
                {
                    FunctionName = funcName,
                    Args = funcArgs is not null ? JsonSerializer.Deserialize<object>(funcArgs.ToJsonString()) : null
                };

                try
                {
                    var result = await _dispatcher.DispatchAsync(context, funcName, funcArgs, _conversations, conversationId);
                    log.Result = result;
                    log.Success = true;
                    contents.Add(GeminiRequestBuilder.FunctionResponseTurn(funcName, new JsonObject { ["result"] = JsonSerializer.SerializeToNode(result) }));

                    if (funcName == "transfer_to_human" && context.EscalationNumber is { Length: > 0 })
                    {
                        pendingEscalation = context.EscalationNumber;
                    }
                    else if (funcName == "transfer_to_department")
                    {
                        var deptName = funcArgs?["department_name"]?.GetValue<string>();
                        if (deptName is { Length: > 0 } &&
                            context.DepartmentPhones?.TryGetValue(deptName, out var deptPhone) == true)
                        {
                            pendingEscalation = deptPhone;
                            pendingFallback = context.EscalationNumber; // main number as fallback if dept doesn't answer
                        }
                    }
                    else if (funcName == "create_appointment" &&
                             result is AiCallAssistent.Application.DTOs.AppointmentResponse apt &&
                             apt.AutoTransferNumber is { Length: > 0 })
                    {
                        pendingAutoTransfer = apt.AutoTransferNumber;
                    }
                }
                catch (Exception ex)
                {
                    log.Success = false;
                    log.Error = ex.Message;
                    contents.Add(GeminiRequestBuilder.FunctionResponseTurn(funcName, new JsonObject { ["error"] = ex.Message }));
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
        var requestBytes = GeminiRequestBuilder.BuildRequestBytes(contentsJson, context, config, _dispatcher, _settings);
        var token = await _tokenProvider.GetAccessTokenAsync();
        var url = $"{EndpointBase}/{_settings.Model}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new ByteArrayContent(requestBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var httpResponse = await _http.SendAsync(request);
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

            var textContents = ExtractConversationForSummary(contents);
            if (textContents.Count == 0)
            {
                _logger.LogWarning("SummarizeConversation: no text turns for {ConversationId}", conversationId);
                return string.Empty;
            }

            // textContents starts at the bot's welcome message (first model turn).
            // If there is no user turn after it, no real conversation happened — the caller
            // hung up before speaking. Skip summarization to avoid Gemini hallucinating.
            var hasRealUserTurn = textContents
                .OfType<JsonObject>()
                .Skip(1) // skip the welcome message
                .Any(t => t["role"]?.GetValue<string>() == "user");

            if (!hasRealUserTurn)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("SummarizeConversation: no caller speech for {ConversationId} — skipping", conversationId);
                return string.Empty;
            }

            var requestBytes = BuildSummaryRequestBytes(textContents.ToJsonString());
            var token = await _tokenProvider.GetAccessTokenAsync();
            var url = $"{EndpointBase}/{_settings.Model}:generateContent";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new ByteArrayContent(requestBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var httpResponse = await _http.SendAsync(request);

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
            var summary = ExtractText(root) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(summary))
                _logger.LogWarning("Gemini returned an empty summary for {ConversationId}. Response: {Response}",
                    conversationId, responseJson);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SummarizeConversation threw for {ConversationId}", conversationId);
            return string.Empty;
        }
    }

    public async Task<string> ClassifyCallerAsync(string conversationId)
    {
        try
        {
            var contents = _conversations.Load(conversationId);
            if (contents.Count == 0) return CallerClassification.Overig;

            var textContents = ExtractConversationForSummary(contents);
            var requestBytes = BuildClassificationRequestBytes(
                textContents.Count > 0 ? textContents.ToJsonString() : contents.ToJsonString());
            var token = await _tokenProvider.GetAccessTokenAsync();
            var url = $"{EndpointBase}/{_settings.Model}:generateContent";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new ByteArrayContent(requestBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var httpResponse = await _http.SendAsync(request);
            if (!httpResponse.IsSuccessStatusCode) return CallerClassification.Overig;

            var responseJson = await httpResponse.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(responseJson);
            var raw = ExtractText(root) ?? string.Empty;

            var classification = raw.Trim().ToLowerInvariant();
            return classification is CallerClassification.Lead
                or CallerClassification.Verkoper
                or CallerClassification.Informatie
                ? classification
                : CallerClassification.Overig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassifyCallerAsync failed for {ConversationId}", conversationId);
            return CallerClassification.Overig;
        }
    }

    // For summarization: strip function-call mechanics, then skip the seeded prolog
    // (appointment-type / department seed turns) so Gemini only sees actual dialogue.
    // The seeded turns leave consecutive user-role entries after stripping, which
    // causes Gemini to return an empty response.
    private static JsonArray ExtractConversationForSummary(JsonArray contents)
    {
        var textOnly = StripFunctionCallTurns(contents);

        // The first model turn is always the welcome message — start there.
        for (var i = 0; i < textOnly.Count; i++)
        {
            if ((textOnly[i] as JsonObject)?["role"]?.GetValue<string>() == "model")
            {
                var result = new JsonArray();
                for (var j = i; j < textOnly.Count; j++)
                    result.Add(textOnly[j]!.DeepClone());
                return result;
            }
        }
        return textOnly;
    }

    private static JsonArray StripFunctionCallTurns(JsonArray contents)
    {
        var result = new JsonArray();
        foreach (var turn in contents)
        {
            if (turn is not JsonObject turnObj) continue;
            var parts = turnObj["parts"]?.AsArray();
            if (parts is null) continue;

            var textParts = new JsonArray();
            foreach (var part in parts)
            {
                if (part?["functionCall"] is null && part?["functionResponse"] is null
                    && part?["thought"]?.GetValue<bool>() != true
                    && part?["text"] is not null)
                {
                    textParts.Add(part!.DeepClone());
                }
            }

            if (textParts.Count > 0)
                result.Add(new JsonObject { ["role"] = turnObj["role"]?.DeepClone(), ["parts"] = textParts });
        }
        return result;
    }

    private static string? ExtractText(JsonNode? root)
    {
        var parts = root?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        return parts?
            .FirstOrDefault(p => p?["thought"]?.GetValue<bool>() != true && p?["text"] is not null)?
            ["text"]?.GetValue<string>();
    }

    private static byte[] BuildClassificationRequestBytes(string contentsJson)
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
            Analyseer het volgende telefoongesprek en classificeer de beller in één van deze categorieën:
            - lead: echte potentiële klant met koopintentie
            - verkoper: vertegenwoordiger of acquiteur die iets wil verkopen
            - informatie: belt alleen voor informatie, geen koopintentie
            - overig: past niet in bovenstaande categorieën

            Antwoord ALLEEN met één woord: lead, verkoper, informatie, of overig.
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

    public async Task<IReadOnlyList<string>> GenerateKnowledgeSuggestionsAsync(string summary, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(summary)) return [];

            var requestBytes = BuildKnowledgeSuggestionsRequestBytes(summary);
            var token = await _tokenProvider.GetAccessTokenAsync(ct);
            var url = $"{EndpointBase}/{_settings.Model}:generateContent";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new ByteArrayContent(requestBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var httpResponse = await _http.SendAsync(request, ct);
            if (!httpResponse.IsSuccessStatusCode) return [];

            var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);
            var root = JsonNode.Parse(responseJson);
            var raw = ExtractText(root) ?? string.Empty;

            raw = raw.Trim();
            var startIdx = raw.IndexOf('[');
            var endIdx = raw.LastIndexOf(']');
            if (startIdx < 0 || endIdx <= startIdx) return [];

            var jsonArray = JsonNode.Parse(raw[startIdx..(endIdx + 1)])?.AsArray();
            if (jsonArray is null) return [];

            return [..jsonArray
                .Select(n => n?.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateKnowledgeSuggestionsAsync failed");
            return [];
        }
    }

    private static byte[] BuildKnowledgeSuggestionsRequestBytes(string summary)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 2048);
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WritePropertyName("contents");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WritePropertyName("parts");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("text",
            $"Gegeven deze Nederlandse gespreksamenvatting, stel 0 tot 3 korte verbeteringen voor die een bedrijf aan hun kennisbank kan toevoegen zodat de AI-assistent soortgelijke vragen beter kan beantwoorden. " +
            $"Geef het resultaat als een JSON-array van eenvoudige Nederlandse strings. Als er niets ontbreekt, geef dan []. Samenvatting: {summary}");
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

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
