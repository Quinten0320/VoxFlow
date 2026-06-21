using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_request")]
public class CompanyRequest
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    /// <summary>"language" | "integration"</summary>
    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("value")]
    public string Value { get; set; } = string.Empty;

    [Column("note")]
    public string? Note { get; set; }

    /// <summary>"pending" | "done" | "rejected"</summary>
    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
