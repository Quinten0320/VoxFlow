using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public sealed class VertexAiTokenProvider : IVertexAiTokenProvider
{
    private readonly GoogleCredential _credential;

    public VertexAiTokenProvider(IOptions<GeminiSettings> settings)
    {
        var s = settings.Value;
        var raw = string.IsNullOrWhiteSpace(s.ServiceAccountKeyPath)
            ? GoogleCredential.GetApplicationDefault()
            : GoogleCredential.FromFile(s.ServiceAccountKeyPath);

        _credential = raw.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
    }

    public Task<string> GetAccessTokenAsync(CancellationToken ct = default) =>
        ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(cancellationToken: ct);
}
