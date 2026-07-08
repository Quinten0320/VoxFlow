namespace AiCallAssistent.Application.Configuration;

public class DeepgramSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "nova-3";
    public string BaseUrl { get; set; } = "wss://api.eu.deepgram.com";

    /// <summary>"flux" (default), "nova3", or "scribe" (ElevenLabs Scribe v2 Realtime).</summary>
    public string SttProvider { get; set; } = "flux";
}
