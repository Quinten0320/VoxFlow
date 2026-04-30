namespace AiCallAssistent.Application.Configuration;

public class OutlookSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Must match exactly what is registered in Azure Portal → App registrations → Authentication.
    /// Update this (and Azure) every time your ngrok URL changes.
    /// Format: https://YOUR-NGROK-URL/api/integrations/outlook/callback
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Where the browser is sent after a successful Outlook OAuth flow.
    /// Set this to your Lovable frontend URL (e.g. https://your-app.lovable.app).
    /// </summary>
    public string DashboardUrl { get; set; } = string.Empty;
}
