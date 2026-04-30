using System.Text;
using System.Text.Json;
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

        var payload = new
        {
            text,
            model_id = _settings.Model,
            language_code = language ?? _settings.Language,
            voice_settings = new
            {
                stability = 0.5,
                similarity_boost = 0.75
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
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
