namespace AiCallAssistent.Application.Services;

public interface ICallbackService
{
    Task<long> ScheduleCallbackAsync(
        short companyId,
        string callerNumber,
        string callerName,
        string reason,
        DateTimeOffset scheduledFrom,
        DateTimeOffset scheduledUntil);
}
