namespace AiCallAssistent.Application.Services;

public interface IDeepgramService
{
    Task<string> TranscribeAsync(byte[] audioBytes, string contentType = "audio/mpeg", string language = "nl");
}
