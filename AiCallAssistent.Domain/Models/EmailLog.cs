using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("email_log")]
public class EmailLog
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public short? CompanyId { get; set; }

    [Column("email_type")]
    public string EmailType { get; set; } = "";

    [Column("to_email")]
    public string ToEmail { get; set; } = "";

    [Column("sent_at")]
    public DateTimeOffset SentAt { get; set; }
}
