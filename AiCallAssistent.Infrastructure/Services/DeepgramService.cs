using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class DeepgramService : IDeepgramService
{
    private readonly HttpClient _http;
    private readonly DeepgramSettings _settings;

    private const string ListenUrl = "https://api.deepgram.com/v1/listen";

    public DeepgramService(HttpClient http, IOptions<DeepgramSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    public async Task<string> TranscribeAsync(byte[] audioBytes, string contentType = "audio/mpeg", string language = "nl")
    {
        var url = $"{ListenUrl}?model={_settings.Model}&language={language}&smart_format=true";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", _settings.ApiKey);
        request.Content = new ByteArrayContent(audioBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Deepgram {(int)response.StatusCode}: {body}", null, response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json);

        return root?["results"]?["channels"]?[0]?["alternatives"]?[0]?["transcript"]
                   ?.GetValue<string>() ?? string.Empty;
    }
}
