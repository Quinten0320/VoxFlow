namespace AiCallAssistent.Application.Services;

public interface IWhatsAppService
{
    Task SendAppointmentConfirmationAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType);

    Task SendAppointmentReminderAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType);

    Task SendAppointmentDayReminderAsync(short companyId, string toNumber,
        string companyName, string? callerName, DateTimeOffset startTime, string serviceType);

    Task SendCallbackConfirmationAsync(short companyId, string toNumber,
        string? callerName);

    Task SendAppointmentFollowupAsync(short companyId, string toNumber,
        string companyName, string? callerName);
}
