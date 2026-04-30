namespace AiCallAssistent.Application.DTOs;

public class CreateAppointmentRequest
{
    public short CompanyId { get; set; }
    public long? EmployeeId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
}
