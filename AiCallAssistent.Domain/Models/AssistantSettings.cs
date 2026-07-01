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

    /// <summary>True when the company has requested WhatsApp activation (admin still needs to register the number with Meta).</summary>
    [Column("whatsapp_requested")]
    public bool WhatsAppRequested { get; set; } = false;

    [Column("whatsapp_requested_at")]
    public DateTimeOffset? WhatsAppRequestedAt { get; set; }

    /// <summary>True once the admin has confirmed the number is registered with Meta and activated WhatsApp for this company.</summary>
    [Column("whatsapp_active")]
    public bool WhatsAppActive { get; set; } = false;

    [Column("tone")]
    public string? Tone { get; set; }

    /// <summary>JSON blob: routing rules per topic (branchTopics + customTopics from onboarding).</summary>
    [Column("routing_rules", TypeName = "jsonb")]
    public string? RoutingRules { get; set; }

    /// <summary>JSON blob: auto-message enabled flags + custom bodies + timing.</summary>
    [Column("auto_message_config", TypeName = "jsonb")]
    public string? AutoMessageConfig { get; set; }

    /// <summary>JSON blob: notification channel preferences.</summary>
    [Column("notification_config", TypeName = "jsonb")]
    public string? NotificationConfig { get; set; }

    [Column("assistant_name")]
    public string? AssistantName { get; set; }

    /// <summary>UI-selected voice key, e.g. "v-emma". Distinct from voice_id which is a numeric ElevenLabs ID.</summary>
    [Column("voice_key")]
    public string? VoiceKey { get; set; }

    [Column("wait_time")]
    public string? WaitTime { get; set; }

    /// <summary>JSON array of all configured escalation/forward numbers.</summary>
    [Column("forward_numbers", TypeName = "jsonb")]
    public string? ForwardNumbers { get; set; }

    [Column("auto_time_greeting")]
    public bool AutoTimeGreeting { get; set; } = false;

    [Column("use_caller_name")]
    public bool UseCallerName { get; set; } = false;

    /// <summary>JSON array: topics the bot is explicitly allowed to handle.</summary>
    [Column("topics_yes", TypeName = "jsonb")]
    public string? TopicsYes { get; set; }

    /// <summary>JSON array: topics the bot must refuse.</summary>
    [Column("topics_no", TypeName = "jsonb")]
    public string? TopicsNo { get; set; }

    /// <summary>What the bot does after refusing a topicsNo topic: "terugbellen"|"doorverbinden"|"kennisbank".</summary>
    [Column("fallback_behavior")]
    public string? FallbackBehavior { get; set; }

    [Column("behavior_instructions")]
    public string? BehaviorInstructions { get; set; }

    /// <summary>Maximum number of appointments per calendar day. Null or 0 = unlimited.</summary>
    [Column("max_appointments_per_day")]
    public int? MaxAppointmentsPerDay { get; set; }

    /// <summary>Selected avatar ID, e.g. "av-sophie". Stored so it persists across sessions.</summary>
    [Column("avatar")]
    public string? Avatar { get; set; }
}
