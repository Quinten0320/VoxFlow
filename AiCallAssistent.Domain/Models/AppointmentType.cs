using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("appointment_types")]
public class AppointmentType
{
    [Key]
    [Column("appointment_type_id")]
    public long AppointmentTypeId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("duration_minutes")]
    public int DurationMinutes { get; set; }

    [Column("wait_time")]
    public short WaitTime { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("location")]
    public string? Location { get; set; }

    /// <summary>When true the bot offers to transfer the caller to a human when the customer requests it.</summary>
    [Column("transfer_on_request")]
    public bool TransferOnRequest { get; set; } = false;

    /// <summary>When true the bot offers to schedule a callback when the customer requests it.</summary>
    [Column("callback_on_request")]
    public bool CallbackOnRequest { get; set; } = false;

    /// <summary>When true the bot may transfer the caller outside opening hours for this appointment type (e.g. urgent cases or on customer request).</summary>
    [Column("transfer_outside_hours")]
    public bool TransferOutsideHours { get; set; } = false;

    /// <summary>After-hours behaviour for this type: "A" = transfer+callback, "B" = callback only. Overrides global setting when set.</summary>
    [Column("after_hours_mode")]
    public string? AfterHoursMode { get; set; }

    /// <summary>When true, an urgent topic always triggers a live transfer even outside opening hours.</summary>
    [Column("urgent_always_forward")]
    public bool UrgentAlwaysForward { get; set; } = false;

    /// <summary>Price in euros, used for revenue insights in reporting.</summary>
    [Column("price", TypeName = "numeric(10,2)")]
    public decimal? Price { get; set; }

    /// <summary>Free-text cancellation policy shown to callers.</summary>
    [Column("cancellation_policy")]
    public string? CancellationPolicy { get; set; }

    /// <summary>JSON array of weekday abbreviations when this type can be booked, e.g. ["Ma","Di","Wo","Do","Vr"].</summary>
    [Column("available_days", TypeName = "jsonb")]
    public string? AvailableDays { get; set; }

    /// <summary>Earliest time this type can be booked (HH:mm), e.g. "09:00".</summary>
    [Column("available_from")]
    public string? AvailableFrom { get; set; }

    /// <summary>Latest time this type can be booked (HH:mm), e.g. "17:00".</summary>
    [Column("available_to")]
    public string? AvailableTo { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }

    public ICollection<AppointmentTypeEmployee> AppointmentTypeEmployees { get; set; } = [];
}
