using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("audit_log")]
public class AuditLog
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("actor_email")]
    public string? ActorEmail { get; set; }

    [Column("action")]
    public string Action { get; set; } = "";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
