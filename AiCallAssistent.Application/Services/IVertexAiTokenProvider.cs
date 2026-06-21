namespace AiCallAssistent.Application.Services;

public interface IVertexAiTokenProvider
{
    /// <summary>Returns a valid OAuth2 Bearer token for Vertex AI. Token is cached and refreshed automatically.</summary>
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
