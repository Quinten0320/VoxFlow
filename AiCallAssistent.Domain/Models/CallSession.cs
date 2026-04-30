using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("call_sessions")]
public class CallSession
{
    [Key]
    [Column("call_sid")]
    public string CallSid { get; set; } = string.Empty;

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Column("caller_number")]
    public string CallerNumber { get; set; } = string.Empty;

    [Column("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [Column("ended_at")]
    public DateTimeOffset? EndedAt { get; set; }

    [Column("status")]
    public string Status { get; set; } = string.Empty;

    [Column("summary")]
    public string? Summary { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
