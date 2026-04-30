namespace AiCallAssistent.Application.DTOs;

public class AppointmentTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
}
