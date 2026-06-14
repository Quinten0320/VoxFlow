namespace AiCallAssistent.Application.DTOs;

public class AppointmentResponse
{
    public long AppointmentId { get; set; }
    public short CompanyId { get; set; }
    public long EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }

    /// <summary>Set when the appointment type has auto-transfer configured.</summary>
    public string? AutoTransferNumber { get; set; }
}
