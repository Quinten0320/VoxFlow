using AiCallAssistent.Application.Services;

namespace AiCallAssistent.Infrastructure.Services;

/// <summary>
/// Stub implementation — replace with a real Funda/NVM API client when available.
/// </summary>
public class StubPropertyInfoProvider : IPropertyInfoProvider
{
    public Task<PropertyInfoResult?> GetPropertyInfoAsync(string query)
    {
        // TODO: integrate with Funda or NVM API
        return Task.FromResult<PropertyInfoResult?>(null);
    }
}
