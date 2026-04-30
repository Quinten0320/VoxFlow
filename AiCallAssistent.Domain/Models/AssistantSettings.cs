using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("assistant_settings")]
public class AssistantSettings
{
    /// <summary>
    /// company_id is both the PK and the FK — 1-to-1 relationship with company.
    /// </summary>
    [Key]
    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("voice_id")]
    public long? VoiceId { get; set; }

    [Column("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [Column("language")]
    public string Language { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("greetings_message")]
    public string? GreetingsMessage { get; set; }

    [Column("appointments_automatically_to_calendar")]
    public bool AppointmentsAutomaticallyToCalendar { get; set; } = false;
}
