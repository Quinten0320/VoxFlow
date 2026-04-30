namespace AiCallAssistent.Application.Configuration;

public class TwilioSettings
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>Public base URL used to build webhook callback URLs (ngrok in dev).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>E.164 Twilio WhatsApp-enabled number. Leave empty to disable WhatsApp messaging.</summary>
    public string WhatsAppFrom { get; set; } = string.Empty;
}
