// Gemini streaming service with exponential backoff retry for 429/503 responses.
using System.Buffers;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class GeminiStreamingService : IGeminiStreamingService
{
    private readonly HttpClient _http;
    private readonly IGeminiFunctionDispatcher _dispatcher;
    private readonly IConversationStore _conversations;
    private readonly IVertexAiTokenProvider _tokenProvider;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiStreamingService> _logger;

    private const int MaxIterations = 8;

    private string EndpointBase =>
        $"https://{_settings.Location}-aiplatform.googleapis.com/v1" +
        $"/projects/{_settings.ProjectId}/locations/{_settings.Location}" +
        $"/publishers/google/models";

    public GeminiStreamingService(
        HttpClient http,
        IGeminiFunctionDispatcher dispatcher,
        IConversationStore conversations,
        IVertexAiTokenProvider tokenProvider,
        IOptions<GeminiSettings> settings,
        ILogger<GeminiStreamingService> logger)
    {
        _http = http;
        _dispatcher = dispatcher;
        _conversations = conversations;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<GeminiStreamResult> RunConversationStreamingAsync(
        CallDispatchContext context,
        string userMessage,
        string conversationId,
        CompanyCallConfig? config,
        CancellationToken ct)
    {
        string? pendingEscalation = null;
        string? pendingFallback = null;
        string? pendingAutoTransfer = null;

        var contents = _conversations.Load(conversationId);
        contents.Add(GeminiRequestBuilder.UserTurn(userMessage));

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var contentsJson = contents.ToJsonString();
            var response = await CallGeminiBatchAsync(contentsJson, context, config, ct);

            if (!response.Success)
            {
                _logger.LogError("Gemini batch error on iteration {Iter}: {Error}", iteration, response.Error);
                return ErrorResult(response.Error!);
            }

            var part = response.Part!;

            if (part["text"] is not null)
            {
                // Pre-save so the status-callback summarizer always has data even if the SSE
                // stream is abandoned mid-way (e.g. caller hangs up while bot is speaking).
                _conversations.Save(conversationId, contents);
                _logger.LogDebug("Gemini streaming text for {ConversationId}", conversationId);
                var textStream = StreamSseResponseAsync(contentsJson, context, config, contents, conversationId, ct);
                return new GeminiStreamResult(true, null, textStream, pendingEscalation, pendingFallback, pendingAutoTransfer);
            }

            if (part["functionCall"] is JsonObject functionCall)
            {
                var funcName = functionCall["name"]!.GetValue<string>();
                var funcArgs = functionCall["args"];

                contents.Add(GeminiRequestBuilder.ModelFunctionCallTurn(functionCall));

                try
                {
                    var result = await _dispatcher.DispatchAsync(context, funcName, funcArgs);
                    contents.Add(GeminiRequestBuilder.FunctionResponseTurn(
                        funcName, new JsonObject { ["result"] = JsonSerializer.SerializeToNode(result) }));

                    if (funcName == "transfer_to_human" && context.EscalationNumber is { Length: > 0 })
                        pendingEscalation = context.EscalationNumber;
                    else if (funcName == "transfer_to_department")
                    {
                        var deptName = funcArgs?["department_name"]?.GetValue<string>();
                        if (deptName is { Length: > 0 } &&
                            context.DepartmentPhones?.TryGetValue(deptName, out var deptPhone) == true)
                        {
                            pendingEscalation = deptPhone;
                            pendingFallback = context.EscalationNumber;
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
                    _logger.LogWarning(ex, "Function {Func} threw for {ConversationId}", funcName, conversationId);
                    contents.Add(GeminiRequestBuilder.FunctionResponseTurn(
                        funcName, new JsonObject { ["error"] = ex.Message }));
                }

                continue;
            }

            return ErrorResult($"Unexpected response part: {part.ToJsonString()}");
        }

        return ErrorResult("Max iterations reached without text response.");
    }

    // ── SSE streaming final response ─────────────────────────────────────────

    private async IAsyncEnumerable<string> StreamSseResponseAsync(
        string contentsJson,
        CallDispatchContext context,
        CompanyCallConfig? config,
        JsonArray contents,
        string conversationId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var bearerToken = await _tokenProvider.GetAccessTokenAsync(ct);
        var url = $"{EndpointBase}/{_settings.Model}:streamGenerateContent?alt=sse";
        var requestBytes = GeminiRequestBuilder.BuildRequestBytes(contentsJson, context, config, _dispatcher, _settings);

        int[] sseDelays = [0, 3, 7];
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < sseDelays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(sseDelays[attempt]), ct);

            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new ByteArrayContent(requestBytes);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            try
            {
                response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini SSE request failed for {ConversationId}", conversationId);
                yield break;
            }

            if ((int)response.StatusCode is 429 or 503)
            {
                _logger.LogWarning("Gemini SSE {Status} on attempt {Attempt}, retrying…",
                    (int)response.StatusCode, attempt + 1);
                response.Dispose();
                response = null;
                continue;
            }
            break;
        }

        if (response is null)
        {
            _logger.LogError("Gemini SSE unavailable after retries for {ConversationId}", conversationId);
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Gemini SSE {Status} for {ConversationId}: {Body}",
                (int)response.StatusCode, conversationId, body);
            response.Dispose();
            yield break;
        }

        var sb = new StringBuilder();

        var sseChunks = 0;
        await using (var stream = await response.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                var json = line["data: ".Length..];
                if (json is "[DONE]") break;

                sseChunks++;
                string? token;
                try
                {
                    var root = JsonNode.Parse(json);
                    token = root?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();

                    // Log non-text SSE chunks (function calls, safety blocks, finish reasons without text)
                    if (token is null or { Length: 0 })
                    {
                        var finishReason = root?["candidates"]?[0]?["finishReason"]?.GetValue<string>();
                        var hasFuncCall = root?["candidates"]?[0]?["content"]?["parts"]?[0]?["functionCall"] is not null;
                        if (hasFuncCall || finishReason is not null)
                            _logger.LogWarning("Gemini SSE non-text chunk for {ConversationId}: functionCall={HasFunc} finishReason={Reason}",
                                conversationId, hasFuncCall, finishReason ?? "null");
                    }
                }
                catch { continue; }

                if (token is { Length: > 0 })
                {
                    sb.Append(token);
                    yield return token;
                }
            }
        }

        response.Dispose();

        if (sb.Length == 0)
            _logger.LogWarning("Gemini SSE yielded 0 text tokens for {ConversationId} ({SseChunks} raw SSE chunks received)",
                conversationId, sseChunks);

        // Persist model turn after full stream is consumed
        if (sb.Length > 0)
        {
            contents.Add(GeminiRequestBuilder.ModelTextTurn(sb.ToString()));
            _conversations.Save(conversationId, contents);
            _logger.LogDebug("Gemini SSE complete for {ConversationId}: {Chars} chars", conversationId, sb.Length);
        }
    }

    // ── Batch helper ─────────────────────────────────────────────────────────

    private async Task<GeminiBatchResult> CallGeminiBatchAsync(
        string contentsJson, CallDispatchContext context, CompanyCallConfig? config, CancellationToken ct)
    {
        var requestBytes = GeminiRequestBuilder.BuildRequestBytes(contentsJson, context, config, _dispatcher, _settings);
        var token = await _tokenProvider.GetAccessTokenAsync(ct);
        var url = $"{EndpointBase}/{_settings.Model}:generateContent";

        int[] batchDelays = [0, 5, 10, 20];
        for (var attempt = 0; attempt < batchDelays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(batchDelays[attempt]), ct);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new ByteArrayContent(requestBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var httpResponse = await _http.SendAsync(request, ct);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);

            if ((int)httpResponse.StatusCode is 503 or 429)
            {
                _logger.LogWarning("Gemini {Status} on attempt {Attempt}, retrying…",
                    (int)httpResponse.StatusCode, attempt + 1);
                continue;
            }

            if (!httpResponse.IsSuccessStatusCode)
                return GeminiBatchResult.Fail($"Gemini API error {(int)httpResponse.StatusCode}: {responseJson}");

            var root = JsonNode.Parse(responseJson);
            var firstPart = root?["candidates"]?[0]?["content"]?["parts"]?[0];

            if (firstPart is null)
            {
                _logger.LogError("Gemini response had no parts for streaming. Full: {Response}", responseJson);
                return GeminiBatchResult.Fail("Gemini response had no parts.");
            }

            return GeminiBatchResult.Ok(firstPart);
        }

        return GeminiBatchResult.Fail("Gemini API unavailable after 4 attempts.");
    }

    private static GeminiStreamResult ErrorResult(string error) =>
        new(false, error, AsyncEnumerable.Empty<string>(), null, null, null);

    private record GeminiBatchResult(bool Success, string? Error, JsonNode? Part)
    {
        public static GeminiBatchResult Ok(JsonNode part) => new(true, null, part);
        public static GeminiBatchResult Fail(string error) => new(false, error, null);
    }
}
