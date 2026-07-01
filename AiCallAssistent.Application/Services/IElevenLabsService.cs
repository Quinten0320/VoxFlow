namespace AiCallAssistent.Application.Services;

public interface IElevenLabsService
{
    Task<byte[]> SynthesizeAsync(string text, string? language = null, string? voiceKey = null);
}
