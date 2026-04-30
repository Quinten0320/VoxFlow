namespace AiCallAssistent.Application.Services;

public interface IWhatsAppService
{
    /// <summary>
    /// Sends a WhatsApp message via Twilio. Never throws — failures are logged and swallowed.
    /// No-ops if Twilio:WhatsAppFrom is not configured.
    /// </summary>
    Task SendAsync(string toNumber, string message);

    Task SendAppointmentConfirmationAsync(
        string toNumber, string displayName, DateTimeOffset startTime, string employeeName);

    Task SendAppointmentReminderAsync(
        string toNumber, string displayName, DateTimeOffset startTime,
        string employeeName, string companyName);
}
