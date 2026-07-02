using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("appointments")]
public class Appointment
{
    [Key]
    [Column("appointment_id")]
    public long AppointmentId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("employee_id")]
    public long EmployeeId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("start_time")]
    public DateTimeOffset StartTime { get; set; }

    [Column("end_time")]
    public DateTimeOffset EndTime { get; set; }

    [Column("caller_phone_number")]
    public string? CallerPhoneNumber { get; set; }

    [Column("reminder_sent_at")]
    public DateTimeOffset? ReminderSentAt { get; set; }

    [Column("followup_sent_at")]
    public DateTimeOffset? FollowupSentAt { get; set; }

    [Column("day_of_reminder_sent_at")]
    public DateTimeOffset? DayOfReminderSentAt { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    public Employee? Employee { get; set; }
}
