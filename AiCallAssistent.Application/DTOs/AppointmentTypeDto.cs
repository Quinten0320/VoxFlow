namespace AiCallAssistent.Application.DTOs;

public class AppointmentTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    /// <summary>Days of lead time required before this appointment can be scheduled.</summary>
    public short WaitTime { get; set; }
    /// <summary>When true: ALWAYS transfer the caller to a human — NEVER attempt to book this appointment type. Immediately trigger a transfer action.</summary>
    public bool TransferOnRequest { get; set; }
    /// <summary>When true: ALWAYS schedule a callback request — NEVER attempt to book this appointment type. Immediately trigger create_callback_request.</summary>
    public bool CallbackOnRequest { get; set; }
    /// <summary>When true: even if the company is currently outside opening hours, you may offer to transfer the caller to a human for this appointment type (e.g. urgent or on explicit customer request). When false: outside opening hours, do NOT transfer — only offer a callback or next-day booking.</summary>
    public bool TransferOutsideHours { get; set; }
}
