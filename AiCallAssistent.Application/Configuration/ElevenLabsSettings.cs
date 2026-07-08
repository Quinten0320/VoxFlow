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

    // ── Scribe v2 Realtime STT ────────────────────────────────────────────────
    // Used only when Deepgram:SttProvider = "scribe". Reuses ApiKey above (same
    // ElevenLabs account as TTS). See ElevenLabsScribeStreamingService.

    /// <summary>Base host for the Scribe realtime WebSocket.</summary>
    public string ScribeBaseUrl { get; set; } = "wss://api.elevenlabs.io";

    /// <summary>Scribe realtime model id.</summary>
    public string ScribeModel { get; set; } = "scribe_v2_realtime";

    /// <summary>
    /// Language hint for Scribe STT. Empty (default) = auto-detect across all supported
    /// languages, so callers speaking any language are still transcribed. Set e.g. "nl"
    /// to force Dutch (disables detection of other languages).
    /// </summary>
    public string ScribeLanguage { get; set; } = "";
}
