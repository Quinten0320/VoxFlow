using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("integration_notify_requests")]
public class IntegrationNotifyRequest
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("integration_key")]
    public string IntegrationKey { get; set; } = "";

    [Column("integration_name")]
    public string IntegrationName { get; set; } = "";

    [Column("resolved")]
    public bool Resolved { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("resolved_at")]
    public DateTimeOffset? ResolvedAt { get; set; }
}
