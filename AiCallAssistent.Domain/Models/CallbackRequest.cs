using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("callback_requests")]
public class CallbackRequest
{
    [Key]
    [Column("callback_request_id")]
    public long CallbackRequestId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("caller_number")]
    public string CallerNumber { get; set; } = string.Empty;

    [Column("caller_name")]
    public string CallerName { get; set; } = string.Empty;

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("scheduled_from")]
    public DateTimeOffset ScheduledFrom { get; set; }

    [Column("scheduled_until")]
    public DateTimeOffset ScheduledUntil { get; set; }

    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
