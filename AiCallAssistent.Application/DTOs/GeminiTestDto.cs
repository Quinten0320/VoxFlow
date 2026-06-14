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
    /// Set when Gemini dispatched transfer_to_human or transfer_to_department this turn.
    /// The TwilioController replaces the &lt;Record&gt; verb with &lt;Dial&gt; to this number.
    /// </summary>
    public string? EscalationNumber { get; set; }

    /// <summary>
    /// Set when transferring to a department. If the department does not answer,
    /// Twilio falls back to this number (typically the main escalation number).
    /// </summary>
    public string? FallbackNumber { get; set; }

    /// <summary>
    /// Set when create_appointment completed and the appointment type has auto-transfer enabled.
    /// The TwilioController plays the reply audio then dials this number.
    /// </summary>
    public string? AutoTransferNumber { get; set; }
}

public class GeminiFunctionCallLog
{
    public string FunctionName { get; set; } = string.Empty;
    public object? Args { get; set; }
    public object? Result { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
