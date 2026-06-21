using System.Text.Json;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AiCallAssistent.Infrastructure.Services.Email;

public class EmailSender(
    IEmailService emailService,
    IServiceScopeFactory scopeFactory)
{
    public async Task SendNowAsync(short? companyId, string toEmail, string toName,
                                   string emailType, string subject, string htmlBody)
    {
        await emailService.SendAsync(toEmail, toName, subject, htmlBody);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EmailLogs.Add(new EmailLog
        {
            CompanyId = companyId,
            EmailType = emailType,
            ToEmail   = toEmail,
            SentAt    = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task<bool> AlreadySentAsync(short companyId, string emailType, TimeSpan window)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var since = DateTimeOffset.UtcNow - window;
        return await db.EmailLogs.AnyAsync(l =>
            l.CompanyId == companyId &&
            l.EmailType == emailType &&
            l.SentAt    >= since);
    }

    public async Task<long> ScheduleAsync(short companyId, string emailType,
                                          object payload, DateTimeOffset scheduledAt)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new EmailJob
        {
            CompanyId   = companyId,
            EmailType   = emailType,
            Payload     = JsonSerializer.Serialize(payload),
            ScheduledAt = scheduledAt,
            Status      = "pending",
            CreatedAt   = DateTimeOffset.UtcNow,
        };
        db.EmailJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    public async Task CancelJobAsync(long jobId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.EmailJobs.FindAsync(jobId);
        if (job is { Status: "pending" })
        {
            job.Status = "cancelled";
            await db.SaveChangesAsync();
        }
    }

    public async Task CancelJobsAsync(short companyId, string emailType)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.EmailJobs
            .Where(j => j.CompanyId == companyId && j.EmailType == emailType && j.Status == "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "cancelled"));
    }
}
