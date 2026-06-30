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

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }

    public ICollection<AppointmentTypeEmployee> AppointmentTypeEmployees { get; set; } = [];
}
