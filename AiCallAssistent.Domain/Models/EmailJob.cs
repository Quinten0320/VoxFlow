using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("email_job")]
public class EmailJob
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short? CompanyId { get; set; }

    [Column("email_type")]
    public string EmailType { get; set; } = "";

    [Column("payload")]
    public string Payload { get; set; } = "{}";

    [Column("scheduled_at")]
    public DateTimeOffset ScheduledAt { get; set; }

    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
