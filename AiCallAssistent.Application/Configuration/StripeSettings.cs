namespace AiCallAssistent.Application.Configuration;

public class StripeSettings
{
    public string SecretKey           { get; set; } = "";
    public string WebhookSecret       { get; set; } = "";
    public string StartMonthlyPriceId { get; set; } = "";
    public string StartYearlyPriceId  { get; set; } = "";
    public string BasisMonthlyPriceId { get; set; } = "";
    public string BasisYearlyPriceId  { get; set; } = "";
    public string GroeiMonthlyPriceId { get; set; } = "";
    public string GroeiYearlyPriceId  { get; set; } = "";
}
