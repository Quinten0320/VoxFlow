using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("whatsapp_message_log")]
public class WhatsAppMessageLog
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("message_type")]
    public string MessageType { get; set; } = string.Empty;

    [Column("to_number")]
    public string ToNumber { get; set; } = string.Empty;

    [Column("sent_at")]
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
