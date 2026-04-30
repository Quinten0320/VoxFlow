using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_opening_exceptions")]
public class CompanyOpeningException
{
    [Key]
    [Column("exception_id")]
    public long ExceptionId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("exception_date")]
    public DateOnly ExceptionDate { get; set; }

    /// <summary>true = fully closed that day. false = use exception time ranges instead of regular hours.</summary>
    [Column("is_closed")]
    public bool IsClosed { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    public ICollection<CompanyOpeningExceptionTimeRange> TimeRanges { get; set; } = [];
}
