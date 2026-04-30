using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("phone_numbers")]
public class PhoneNumber
{
    [Key]
    [Column("phone_number_id")]
    public long PhoneNumberId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("escalation_phone_number")]
    public string? EscalationPhoneNumber { get; set; }

    [Column("ai_phone_number")]
    public string AiPhoneNumber { get; set; } = string.Empty;

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
