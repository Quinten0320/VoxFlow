using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_opening_exception_time_ranges")]
public class CompanyOpeningExceptionTimeRange
{
    [Key]
    [Column("exception_time_range_id")]
    public long ExceptionTimeRangeId { get; set; }

    [Column("exception_id")]
    public long ExceptionId { get; set; }

    [Column("start_time")]
    public TimeOnly StartTime { get; set; }

    [Column("end_time")]
    public TimeOnly EndTime { get; set; }

    [Column("sort_order")]
    public short SortOrder { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [ForeignKey(nameof(ExceptionId))]
    public CompanyOpeningException? Exception { get; set; }
}
