namespace AiCallAssistent.Application.Services;

public interface IWhatsAppService
{
    /// <summary>
    /// Sends a WhatsApp message via Twilio. Never throws — failures are logged and swallowed.
    /// No-ops if Twilio:WhatsAppFrom is not configured.
    /// </summary>
    Task SendAsync(string toNumber, string message, string? fromNumber = null);

    Task SendAppointmentConfirmationAsync(
        string toNumber, string displayName, DateTimeOffset startTime, string employeeName,
        string? fromNumber = null);

    Task SendAppointmentReminderAsync(
        string toNumber, string displayName, DateTimeOffset startTime,
        string employeeName, string companyName, string? fromNumber = null);

    Task SendCallbackConfirmationAsync(
        string toNumber, string callerName, DateTimeOffset scheduledFrom, DateTimeOffset scheduledUntil,
        string? fromNumber = null);

    Task SendAppointmentDayReminderAsync(
        string toNumber, string displayName, DateTimeOffset startTime,
        string employeeName, string companyName, string? fromNumber = null);

    Task SendAppointmentFollowupAsync(
        string toNumber, string displayName, string companyName, string? fromNumber = null);
}
