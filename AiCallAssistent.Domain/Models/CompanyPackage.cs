using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiCallAssistent.Domain.Models;

[Table("company_package")]
public class CompanyPackage
{
    [Key]
    [Column("company_id")]
    public short CompanyId { get; set; }

    // Limits — null means unlimited
    [Column("max_call_minutes")]
    public int? MaxCallMinutes { get; set; }

    [Column("max_whatsapp_per_month")]
    public int? MaxWhatsAppPerMonth { get; set; }

    // Feature flags
    [Column("feature_blacklist")]
    public bool FeatureBlacklist { get; set; } = true;

    [Column("feature_callback_requests")]
    public bool FeatureCallbackRequests { get; set; } = true;

    [Column("feature_whatsapp_confirmation")]
    public bool FeatureWhatsAppConfirmation { get; set; } = true;

    [Column("feature_whatsapp_reminders")]
    public bool FeatureWhatsAppReminders { get; set; } = true;

    [Column("feature_department_routing")]
    public bool FeatureDepartmentRouting { get; set; } = true;

    [Column("feature_transfer_to_human")]
    public bool FeatureTransferToHuman { get; set; } = true;

    [Column("feature_after_hours_mode")]
    public bool FeatureAfterHoursMode { get; set; } = true;

    [Column("feature_branch_tools")]
    public bool FeatureBranchTools { get; set; } = true;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
