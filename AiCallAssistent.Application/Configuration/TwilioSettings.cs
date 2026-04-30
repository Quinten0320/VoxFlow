namespace AiCallAssistent.Application.Configuration;

public class TwilioSettings
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Publicly reachable base URL used to build webhook callback URLs.
    /// Use an ngrok tunnel in development: https://abc123.ngrok.io
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// E.164 Twilio WhatsApp-enabled number, e.g. "+31612345678".
    /// Must be registered as a WhatsApp Sender in the Twilio Console.
    /// Leave empty to disable WhatsApp messaging.
    /// </summary>
    public string WhatsAppFrom { get; set; } = string.Empty;
}
