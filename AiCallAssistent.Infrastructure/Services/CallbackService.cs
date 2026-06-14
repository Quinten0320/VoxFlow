using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;

namespace AiCallAssistent.Infrastructure.Services;

public class CallbackService(AppDbContext db) : ICallbackService
{
    public async Task<long> ScheduleCallbackAsync(
        short companyId,
        string callerNumber,
        string callerName,
        string reason,
        DateTimeOffset scheduledFrom,
        DateTimeOffset scheduledUntil)
    {
        var request = new CallbackRequest
        {
            CompanyId = companyId,
            CallerNumber = callerNumber,
            CallerName = callerName,
            Reason = reason,
            ScheduledFrom = scheduledFrom.ToUniversalTime(),
            ScheduledUntil = scheduledUntil.ToUniversalTime(),
            Status = CallbackStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.CallbackRequests.Add(request);
        await db.SaveChangesAsync();
        return request.CallbackRequestId;
    }
}
