using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("assistant_profiles")]
public class AssistantProfile
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = false;

    [Column("system_prompt")]
    public string? SystemPrompt { get; set; }

    [Column("greeting_message")]
    public string? GreetingMessage { get; set; }

    [Column("language")]
    public string Language { get; set; } = "nl";

    [Column("call_mode")]
    public string CallMode { get; set; } = "first_line";

    [Column("after_hours_mode")]
    public string? AfterHoursMode { get; set; }

    [Column("bot_active_hours_enabled")]
    public bool BotActiveHoursEnabled { get; set; } = false;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }

    public ICollection<BotActiveHour> BotActiveHours { get; set; } = [];
}
