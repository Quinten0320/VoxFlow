namespace AiCallAssistent.Application.Configuration;

public class ElevenLabsSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string VoiceId { get; set; } = string.Empty;
    public string Model { get; set; } = "eleven_turbo_v2_5";
    public string Language { get; set; } = "nl";

    /// <summary>
    /// Set to true only for models that accept a language_code parameter (e.g. eleven_multilingual_v2, eleven_turbo_v2).
    /// eleven_turbo_v2_5 and flash models auto-detect the language and reject this parameter.
    /// </summary>
    public bool SendLanguageCode { get; set; } = false;

    /// <summary>0–4. Higher reduces streaming latency at a minor quality cost. 4 recommended for phone calls.</summary>
    public int OptimizeStreamingLatency { get; set; } = 4;
}
