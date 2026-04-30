using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_outlook_tokens")]
public class CompanyOutlookToken
{
    /// <summary>company_id is both the PK and the FK — 1-to-1 with company.</summary>
    [Key]
    [Column("company_id")]
    public short CompanyId { get; set; }

    [Column("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [Column("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [Column("token_expiry")]
    public DateTimeOffset TokenExpiry { get; set; }

    /// <summary>The Microsoft account email that was authorized (for display in the dashboard).</summary>
    [Column("outlook_email")]
    public string? OutlookEmail { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
