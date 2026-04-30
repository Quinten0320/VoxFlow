using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;

namespace AiCallAssistent.Application.Services;

public interface IOutlookCalendarService
{
    /// <summary>
    /// Creates a calendar event in the company's connected Outlook calendar.
    /// No-ops silently if the company has not connected Outlook.
    /// </summary>
    Task CreateEventAsync(short companyId, Appointment appointment, string employeeName,
        string displayName, string? customerName);

    /// <summary>
    /// Fetches calendar events from the company's connected Outlook calendar within the given UTC range.
    /// Returns an empty list if the company has not connected Outlook.
    /// </summary>
    Task<List<OutlookEventDto>> GetEventsAsync(short companyId, DateTimeOffset from, DateTimeOffset to);
}
