namespace AiCallAssistent.Application.Services;

public interface IElevenLabsStreamingService
{
    /// <summary>Streams ulaw_8000 audio chunks for the given text.</summary>
    IAsyncEnumerable<byte[]> StreamAsync(string text, CancellationToken ct);
}
