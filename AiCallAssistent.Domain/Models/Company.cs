using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company")]
public class Company
{
    [Key]
    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("company_name")]
    public string CompanyName { get; set; } = string.Empty;

    [Column("company_type")]
    public short? CompanyType { get; set; }

    [Column("company_info")]
    public string? CompanyInfo { get; set; }

    [Column("package_type")]
    public short? PackageType { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("branch")]
    public string? Branch { get; set; }

    public ICollection<Employee> Employees { get; set; } = [];
}
