namespace AiCallAssistent.Application.Configuration;

public class ElevenLabsSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string VoiceId { get; set; } = string.Empty;
    public string Model { get; set; } = "eleven_turbo_v2_5";
    public string Language { get; set; } = "nl";
}
