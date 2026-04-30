using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_opening_hours")]
public class CompanyOpeningHour
{
    [Key]
    [Column("opening_hour_id")]
    public long OpeningHourId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    /// <summary>1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri, 6=Sat, 7=Sun</summary>
    [Column("day_of_week")]
    public short DayOfWeek { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    public ICollection<CompanyOpeningTimeRange> TimeRanges { get; set; } = [];
}
