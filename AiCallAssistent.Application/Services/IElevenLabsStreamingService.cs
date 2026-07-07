namespace AiCallAssistent.Application.Services;

public interface ITtsStreamingService
{
    /// <summary>Streams ulaw_8000 audio chunks for the given text.</summary>
    /// <param name="voiceId">ElevenLabs voice ID to use. Falls back to global config when null.</param>
    IAsyncEnumerable<byte[]> StreamAsync(string text, CancellationToken ct, string? voiceId = null);
}
