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

    /// <summary>
    /// After-hours behaviour: null/empty = disabled, "A" = try transfer then callback, "B" = direct callback.
    /// </summary>
    [Column("after_hours_mode")]
    public string? AfterHoursMode { get; set; }

    /// <summary>
    /// Call answering mode: "first_line" = bot answers immediately, "backup" = try escalation number first.
    /// </summary>
    [Column("call_mode")]
    public string CallMode { get; set; } = "first_line";

    /// <summary>When true, calls outside configured bot_active_hours windows are rejected/transferred.</summary>
    [Column("bot_active_hours_enabled")]
    public bool BotActiveHoursEnabled { get; set; } = false;

    /// <summary>Twilio WhatsApp-enabled number used as the From number for outbound messages. Falls back to global Twilio:WhatsAppFrom if null.</summary>
    [Column("whatsapp_phone_number")]
    public string? WhatsAppPhoneNumber { get; set; }
}
