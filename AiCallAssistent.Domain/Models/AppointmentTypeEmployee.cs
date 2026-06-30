using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("appointment_type_employees")]
public class AppointmentTypeEmployee
{
    [Column("appointment_type_id")]
    public long AppointmentTypeId { get; set; }

    [Column("employee_id")]
    public long EmployeeId { get; set; }

    [ForeignKey(nameof(AppointmentTypeId))]
    public AppointmentType? AppointmentType { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    public Employee? Employee { get; set; }
}
