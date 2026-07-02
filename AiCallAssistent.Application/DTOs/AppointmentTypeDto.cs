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
    /// <summary>Physical or virtual location where this appointment takes place. Tell the caller after confirming the booking.</summary>
    public string? Location { get; set; }
    /// <summary>Cancellation policy text. Always mention this to the caller after confirming the booking.</summary>
    public string? CancellationPolicy { get; set; }
    /// <summary>When true: ALWAYS transfer to a human immediately via transfer_to_human, even outside opening hours or when urgency is signalled — regardless of the global after-hours setting.</summary>
    public bool UrgentAlwaysForward { get; set; }
    /// <summary>Per-type override of the global after-hours behaviour. "A" = try to transfer (transfer_to_human); "B" = callback only (schedule_callback). Null = use global setting.</summary>
    public string? AfterHoursMode { get; set; }
    /// <summary>Dutch weekday abbreviations on which this type can be booked, e.g. ["Ma","Di","Wo","Do","Vr"]. Null = all days.</summary>
    public string[]? AvailableDays { get; set; }
    /// <summary>Earliest time this type can be booked (HH:mm). Null = start of opening hours.</summary>
    public string? AvailableFrom { get; set; }
    /// <summary>Latest end-time for this type (HH:mm). Null = end of opening hours.</summary>
    public string? AvailableTo { get; set; }
}
