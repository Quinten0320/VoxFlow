namespace AiCallAssistent.Application.Configuration;

public class TwilioSettings
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string ApiKeySid { get; set; } = string.Empty;
    public string ApiKeySecret { get; set; } = string.Empty;

    /// <summary>Public base URL used to build webhook callback URLs.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>E.164 Twilio WhatsApp-enabled number. Leave empty to disable WhatsApp messaging.</summary>
    public string WhatsAppFrom { get; set; } = string.Empty;

    /// <summary>Twilio Verify Service SID (VS...). Used for SMS phone verification during onboarding.</summary>
    public string VerifyServiceSid { get; set; } = string.Empty;

    /// <summary>Twilio REST API base URL.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.twilio.com";
}
