using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("assistant_feedback")]
public class AssistantFeedback
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short? CompanyId { get; set; }

    [Column("message")]
    public string Message { get; set; } = "";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
