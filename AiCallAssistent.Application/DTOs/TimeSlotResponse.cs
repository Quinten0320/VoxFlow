namespace AiCallAssistent.Application.DTOs;

public class TimeSlotResponse
{
    public long EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    /// <summary>Spoken Dutch time to use verbally, e.g. "om kwart over 2 's middags".</summary>
    public string SpokenTime { get; set; } = string.Empty;
}

public class AvailabilityResponse
{
    public short CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public List<TimeSlotResponse> AvailableSlots { get; set; } = [];
}

public class SoonestAvailableResponse
{
    public short CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public TimeSlotResponse? SoonestSlot { get; set; }
}
