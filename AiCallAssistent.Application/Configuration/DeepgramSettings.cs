namespace AiCallAssistent.Application.Configuration;

public class DeepgramSettings
{
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Deepgram model to use for transcription. Default: nova-3.</summary>
    public string Model { get; set; } = "nova-3";
}
