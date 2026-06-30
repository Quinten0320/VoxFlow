namespace AiCallAssistent.Application.DTOs;

public class AppointmentTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    /// <summary>Days of lead time required before this appointment can be scheduled.</summary>
    public short WaitTime { get; set; }
    /// <summary>When true: if the caller requests a human transfer instead of booking, connect them.</summary>
    public bool TransferOnRequest { get; set; }
    /// <summary>When true: if the caller prefers a callback over booking, schedule one.</summary>
    public bool CallbackOnRequest { get; set; }
}
