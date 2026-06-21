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

    [Column("wait_time")]
    public short WaitTime { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("auto_transfer_enabled")]
    public bool AutoTransferEnabled { get; set; } = false;

    [Column("auto_transfer_department_id")]
    public long? AutoTransferDepartmentId { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
