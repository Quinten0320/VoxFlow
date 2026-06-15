using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("employee")]
public class Employee
{
    [Key]
    [Column("employee_id")]
    public long EmployeeId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("is_owner")]
    public bool IsOwner { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Links this employee to a Supabase auth user (auth.users.id).</summary>
    [Column("auth_user_id")]
    public Guid? AuthUserId { get; set; }

    [Column("role")]
    public string? Role { get; set; }

    [Column("phone")]
    public string? Phone { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = [];
}
