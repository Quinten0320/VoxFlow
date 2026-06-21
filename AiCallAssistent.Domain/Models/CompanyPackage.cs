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

    [Column("stripe_customer_id")]
    public string? StripeCustomerId { get; set; }

    [Column("stripe_subscription_id")]
    public string? StripeSubscriptionId { get; set; }

    [Column("subscription_status")]
    public string SubscriptionStatus { get; set; } = "none";

    [Column("trial_ends_at")]
    public DateTimeOffset? TrialEndsAt { get; set; }

    [Column("current_period_end")]
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    [Column("plan_name")]
    public string? PlanName { get; set; }

    [Column("billing_interval")]
    public string? BillingInterval { get; set; }

    [Column("referral_code")]
    public string? ReferralCode { get; set; }

    [Column("referred_by_company_id")]
    public short? ReferredByCompanyId { get; set; }

    [Column("referral_rewarded_at")]
    public DateTimeOffset? ReferralRewardedAt { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }
}
