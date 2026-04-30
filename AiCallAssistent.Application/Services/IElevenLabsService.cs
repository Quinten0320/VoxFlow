namespace AiCallAssistent.Application.Services;

public interface IElevenLabsService
{
    /// <summary>
    /// Synthesizes text to speech and returns the audio as MP3 bytes.
    /// language: BCP-47 code, e.g. "nl" or "en". Defaults to the value in ElevenLabsSettings.
    /// </summary>
    Task<byte[]> SynthesizeAsync(string text, string? language = null);
}
