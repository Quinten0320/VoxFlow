namespace AiCallAssistent.Application.Services;

public interface IAudioStore
{
    /// <summary>Stores audio bytes and returns a unique ID for later retrieval.</summary>
    string Store(byte[] audio, string contentType);

    /// <summary>Retrieves stored audio by ID. Returns null if not found or expired.</summary>
    (byte[] Audio, string ContentType)? Get(string id);
}
