namespace AiCallAssistent.Application.DTOs;

public class GeminiTestRequest
{
    public short CompanyId { get; set; }
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>Pass the conversationId from the previous response to continue a conversation.</summary>
    public string? ConversationId { get; set; }
}

public class GeminiTestResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string FinalResponse { get; set; } = string.Empty;
    public List<GeminiFunctionCallLog> FunctionCalls { get; set; } = [];
    public int Iterations { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public int TotalMessages { get; set; }

    /// <summary>
    /// Set when Gemini dispatched transfer_to_human this turn.
    /// The TwilioController replaces the &lt;Record&gt; verb with &lt;Dial&gt; to this number.
    /// </summary>
    public string? EscalationNumber { get; set; }
}

public class GeminiFunctionCallLog
{
    public string FunctionName { get; set; } = string.Empty;
    public object? Args { get; set; }
    public object? Result { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
