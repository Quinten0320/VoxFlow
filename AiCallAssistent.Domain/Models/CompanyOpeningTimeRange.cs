using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_opening_time_ranges")]
public class CompanyOpeningTimeRange
{
    [Key]
    [Column("opening_time_range_id")]
    public long OpeningTimeRangeId { get; set; }

    [Column("opening_hour_id")]
    public long OpeningHourId { get; set; }

    [Column("start_time")]
    public TimeOnly StartTime { get; set; }

    [Column("end_time")]
    public TimeOnly EndTime { get; set; }

    [Column("sort_order")]
    public short SortOrder { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [ForeignKey(nameof(OpeningHourId))]
    public CompanyOpeningHour? OpeningHour { get; set; }
}
