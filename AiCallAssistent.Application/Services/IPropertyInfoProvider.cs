namespace AiCallAssistent.Application.Services;

public record PropertyInfoResult(
    string Address,
    string? Price,
    string? Description,
    string? Status,
    string? Url);

public interface IPropertyInfoProvider
{
    /// <summary>
    /// Looks up property information by address or listing ID.
    /// Returns null if no listing is found.
    /// </summary>
    Task<PropertyInfoResult?> GetPropertyInfoAsync(string query);
}
