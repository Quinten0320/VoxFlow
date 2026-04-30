namespace AiCallAssistent.Application.Configuration;

public class ElevenLabsSettings
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>ElevenLabs voice ID. Configure in appsettings.json (non-sensitive).</summary>
    public string VoiceId { get; set; } = string.Empty;

    /// <summary>
    /// ElevenLabs model ID.
    /// eleven_multilingual_v2 supports Dutch (nl) and 28 other languages.
    /// Configure in appsettings.json.
    /// </summary>
    public string Model { get; set; } = "eleven_turbo_v2_5";

    /// <summary>BCP-47 language code. Default: nl (Dutch / Nederlands).</summary>
    public string Language { get; set; } = "nl";
}
