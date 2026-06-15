using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("knowledge_suggestions")]
public class KnowledgeSuggestion
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>"new" | "added" | "dismissed"</summary>
    [Column("status")]
    public string Status { get; set; } = "new";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
