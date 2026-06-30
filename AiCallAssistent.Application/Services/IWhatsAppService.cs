namespace AiCallAssistent.Application.Services;

public interface IWhatsAppService
{
    /// <summary>
    /// Company-aware send: checks WhatsAppActive flag, enforces MaxWhatsAppPerMonth quota,
    /// resolves from-number from AssistantSettings, and logs to whatsapp_message_log.
    /// Prefer the typed methods below over calling this directly.
    /// </summary>
    Task SendForCompanyAsync(short companyId, string toNumber, string message, string messageType);

    Task SendAppointmentConfirmationAsync(short companyId, string toNumber,
        DateTimeOffset startTime, string serviceType, string employeeName);

    Task SendAppointmentReminderAsync(short companyId, string toNumber,
        string companyName, DateTimeOffset startTime, string serviceType, string employeeName);

    Task SendAppointmentDayReminderAsync(short companyId, string toNumber,
        string companyName, DateTimeOffset startTime, string serviceType, string employeeName);

    Task SendCallbackConfirmationAsync(short companyId, string toNumber,
        string callerName, DateTimeOffset scheduledFrom, DateTimeOffset scheduledUntil);

    Task SendAppointmentFollowupAsync(short companyId, string toNumber,
        string companyName, string serviceType);
}
