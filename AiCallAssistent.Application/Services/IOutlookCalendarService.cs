using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;

namespace AiCallAssistent.Application.Services;

public interface IOutlookCalendarService
{
    /// <summary>No-op if the company has not connected Outlook.</summary>
    Task CreateEventAsync(short companyId, Appointment appointment, string employeeName,
        string displayName, string? customerName);

    /// <summary>Returns an empty list if the company has not connected Outlook.</summary>
    Task<List<OutlookEventDto>> GetEventsAsync(short companyId, DateTimeOffset from, DateTimeOffset to);
}
