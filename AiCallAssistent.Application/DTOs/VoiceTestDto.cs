namespace AiCallAssistent.Application.DTOs;

public class VoiceTestRequest
{
    /// <summary>The company the assistant is handling calls for.</summary>
    public short CompanyId { get; set; }

    /// <summary>Simulated caller message, e.g. "Ik wil graag een afspraak maken".</summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>Pass the conversationId from the previous response to continue the conversation.</summary>
    public string? ConversationId { get; set; }
}

public class VoiceTestResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>Gemini's final text reply.</summary>
    public string TextResponse { get; set; } = string.Empty;

    /// <summary>
    /// Relative URL to stream the synthesized MP3 audio.
    /// GET this endpoint in a browser or audio player to hear the response.
    /// </summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>Pass this back in the next request to continue the conversation.</summary>
    public string ConversationId { get; set; } = string.Empty;

    public List<GeminiFunctionCallLog> FunctionCalls { get; set; } = [];
}
