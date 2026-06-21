namespace AiCallAssistent.Application.DTOs;

public class AppointmentTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    /// <summary>Days of lead time required before this appointment can be scheduled.</summary>
    public short WaitTime { get; set; }
}
