using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class ElevenLabsService : IElevenLabsService
{
    private readonly HttpClient _http;
    private readonly ElevenLabsSettings _settings;

    private const string BaseUrl = "https://api.elevenlabs.io/v1";

    public ElevenLabsService(HttpClient http, IOptions<ElevenLabsSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
        // xi-api-key is the ElevenLabs authentication header.
        // Safe to set on DefaultRequestHeaders: typed clients each get their own HttpClient instance.
        _http.DefaultRequestHeaders.Add("xi-api-key", _settings.ApiKey);
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? language = null)
    {
        var url = $"{BaseUrl}/text-to-speech/{_settings.VoiceId}";

        // Build payload manually so language_code is only included when the model supports it.
        // eleven_turbo_v2_5 and flash models auto-detect language and reject the parameter.
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
            payload["language_code"] = language ?? _settings.Language;

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(
            payload.ToJsonString(), Encoding.UTF8, "application/json");
        // Accept MP3 audio — ElevenLabs defaults to audio/mpeg when this header is set
        request.Headers.Add("Accept", "audio/mpeg");

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"ElevenLabs {(int)response.StatusCode}: {body}", null, response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync();
    }
}
