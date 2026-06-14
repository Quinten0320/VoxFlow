using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_departments")]
public class CompanyDepartment
{
    [Key]
    [Column("department_id")]
    public long DepartmentId { get; set; }

    [Column("company_id")]
    public short CompanyId { get; set; }

    /// <summary>snake_case key used by Gemini, e.g. "hr", "tech_support".</summary>
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable label shown to callers, e.g. "HR".</summary>
    [Column("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
