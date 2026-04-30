namespace AiCallAssistent.Application.Configuration;

public class OutlookSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Must match the redirect URI registered in Azure App registrations.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    public string DashboardUrl { get; set; } = string.Empty;
}
