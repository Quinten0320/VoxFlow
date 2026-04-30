namespace AiCallAssistent.Application.Services;

public interface IDeepgramService
{
    /// <summary>
    /// Transcribes audio bytes to text using Deepgram Nova.
    /// language: BCP-47 code, e.g. "nl" or "en-US". Defaults to "nl".
    /// </summary>
    Task<string> TranscribeAsync(byte[] audioBytes, string contentType = "audio/mpeg", string language = "nl");
}
