using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class ElevenLabsStreamingService : IElevenLabsStreamingService
{
    private readonly HttpClient _http;
    private readonly ElevenLabsSettings _settings;
    private readonly ILogger<ElevenLabsStreamingService> _logger;

    private const string BaseUrl = "https://api.elevenlabs.io/v1";

    public ElevenLabsStreamingService(
        HttpClient http,
        IOptions<ElevenLabsSettings> settings,
        ILogger<ElevenLabsStreamingService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _http.DefaultRequestHeaders.Add("xi-api-key", _settings.ApiKey);
        _logger = logger;
    }

    public async IAsyncEnumerable<byte[]> StreamAsync(
        string text, [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"{BaseUrl}/text-to-speech/{_settings.VoiceId}/stream"
            + $"?output_format=ulaw_8000"
            + $"&optimize_streaming_latency={_settings.OptimizeStreamingLatency}";

        var payload = new JsonObject
        {
            ["text"]     = text,
            ["model_id"] = _settings.Model,
            ["voice_settings"] = new JsonObject
            {
                ["stability"]        = 0.5,
                ["similarity_boost"] = 0.75
            }
        };

        if (_settings.SendLanguageCode)
            payload["language_code"] = _settings.Language;

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        request.Headers.Add("Accept", "audio/basic");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs streaming request failed");
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("ElevenLabs streaming {Status}: {Body}", (int)response.StatusCode, body);
            response.Dispose();
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var chunkBuffer = new byte[2048];

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(chunkBuffer, ct)) > 0)
        {
            var chunk = new byte[bytesRead];
            chunkBuffer.AsSpan(0, bytesRead).CopyTo(chunk);
            yield return chunk;
        }

        response.Dispose();
    }
}
