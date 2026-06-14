using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("call_blacklist")]
public class CallBlacklist
{
    [Key]
    [Column("blacklist_id")]
    public long BlacklistId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Column("reason")]
    public string? Reason { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
