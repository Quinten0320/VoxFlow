using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("bot_active_hours")]
public class BotActiveHour
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    /// <summary>1=Mon … 7=Sun (same convention as opening_hours).</summary>
    [Column("day_of_week")]
    public short DayOfWeek { get; set; }

    [Column("open_time")]
    public TimeOnly OpenTime { get; set; }

    [Column("close_time")]
    public TimeOnly CloseTime { get; set; }

    /// <summary>Null = belongs to company-level settings; set = belongs to the given profile.</summary>
    [Column("profile_id")]
    public int? ProfileId { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }

    [ForeignKey(nameof(ProfileId))]
    public AssistantProfile? Profile { get; set; }
}
