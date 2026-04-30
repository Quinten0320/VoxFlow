using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("appointment_types")]
public class AppointmentType
{
    [Key]
    [Column("appointment_type_id")]
    public long AppointmentTypeId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("duration_minutes")]
    public int DurationMinutes { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
