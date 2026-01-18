using AiCallAssistent.Infrastructure.ElevenLabs.Models;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;


namespace AiCallAssistent.Infrastructure.ElevenLabs.Services
{
    public class ElevenLabsTextToSpeechService
    {
        private readonly HttpClient _http;
        private readonly ElevenLabsOptions _options;

        public ElevenLabsTextToSpeechService(
            HttpClient http,
            IOptions<ElevenLabsOptions> options)
        {
            _http = http;
            _options = options.Value;
        }

        public async Task<byte[]> SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
                throw new Exception("ElevenLabs ApiKey is leeg");

            if (string.IsNullOrWhiteSpace(_options.VoiceId))
                throw new Exception("ElevenLabs VoiceId is leeg");

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.elevenlabs.io/v1/text-to-speech/{_options.VoiceId}");

            request.Headers.Add("xi-api-key", _options.ApiKey);
            request.Headers.Add("Accept", "audio/mpeg");

            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    text = text,
                    model_id = "eleven_turbo_v2"
                }),
                Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"ElevenLabs error {response.StatusCode}: {body}");
            }

            return await response.Content.ReadAsByteArrayAsync();
        }

    }
}
