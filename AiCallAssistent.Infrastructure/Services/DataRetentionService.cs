using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services;

public class DataRetentionService(
    IServiceScopeFactory scopeFactory,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval      = TimeSpan.FromHours(24);
    private const int CallSessionRetentionDays     = 90;
    private const int SubscriptionDeletionDays     = 30;
    private const int SubscriptionWarningDays      = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a few minutes after startup so DB connections are warm.
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunAllJobsAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "DataRetentionService run failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAllJobsAsync(CancellationToken ct)
    {
        await PurgeOldCallSessionsAsync(ct);
        await SendExpiryWarningsAsync(ct);
        await PurgeExpiredCompaniesAsync(ct);
    }

    // ── Purge call sessions older than 90 days ───────────────────────────────

    private async Task PurgeOldCallSessionsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff  = DateTimeOffset.UtcNow.AddDays(-CallSessionRetentionDays);
        var deleted = await db.CallSessions
            .Where(s => s.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("DataRetention: deleted {Count} call sessions older than {Days} days",
                deleted, CallSessionRetentionDays);
    }

    // ── Send warning email 5 days before the 30-day deletion cutoff ──────────

    private async Task SendExpiryWarningsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender    = scope.ServiceProvider.GetRequiredService<EmailSender>();
        var templates = scope.ServiceProvider.GetRequiredService<EmailTemplateService>();

        // Window: companies whose expiry timestamp is between day 24 and day 26 (warn exactly once)
        var warnFrom = DateTimeOffset.UtcNow.AddDays(-(SubscriptionWarningDays + 1));
        var warnTo   = DateTimeOffset.UtcNow.AddDays(-SubscriptionWarningDays);

        var expiring = await db.CompanyPackages
            .Where(p => p.SubscriptionExpiredAt != null
                     && p.SubscriptionExpiredAt <= warnTo
                     && p.SubscriptionExpiredAt >  warnFrom)
            .Select(p => p.CompanyId)
            .ToListAsync(ct);

        foreach (var companyId in expiring)
        {
            var owner = await db.Employees
                .Where(e => e.CompanyId == companyId && e.IsOwner && e.IsActive && e.Email != null)
                .FirstOrDefaultAsync(ct);

            if (owner?.Email == null) continue;

            try
            {
                var daysLeft = SubscriptionDeletionDays - SubscriptionWarningDays;
                var (subject, html) = templates.SubscriptionExpiredWarning(owner.Name, daysLeft);
                await sender.SendNowAsync(companyId, owner.Email, owner.Name, "subscription_expiry_warning", subject, html);
                logger.LogInformation("DataRetention: sent expiry warning to company {CompanyId}", companyId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DataRetention: failed to send expiry warning to company {CompanyId}", companyId);
            }
        }
    }

    // ── Purge all data for companies 30+ days past expiry ────────────────────

    private async Task PurgeExpiredCompaniesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender    = scope.ServiceProvider.GetRequiredService<EmailSender>();
        var templates = scope.ServiceProvider.GetRequiredService<EmailTemplateService>();

        var deletionCutoff = DateTimeOffset.UtcNow.AddDays(-SubscriptionDeletionDays);

        var toDelete = await db.CompanyPackages
            .Where(p => p.SubscriptionExpiredAt != null
                     && p.SubscriptionExpiredAt <= deletionCutoff)
            .Select(p => p.CompanyId)
            .ToListAsync(ct);

        foreach (var companyId in toDelete)
        {
            // Capture owner info before deletion so we can send confirmation email.
            var owner = await db.Employees
                .Where(e => e.CompanyId == companyId && e.IsOwner && e.IsActive && e.Email != null)
                .Select(e => new { e.Name, e.Email })
                .FirstOrDefaultAsync(ct);

            try
            {
                await DeleteCompanyDataAsync(db, companyId, ct);
                logger.LogInformation("DataRetention: purged all data for company {CompanyId} (AVG 30-day rule)", companyId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DataRetention: failed to purge company {CompanyId}", companyId);
                continue;
            }

            if (owner?.Email == null) continue;
            try
            {
                var (subject, html) = templates.DataDeleted(owner.Name);
                await sender.SendNowAsync(null, owner.Email, owner.Name, "data_deleted", subject, html);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DataRetention: failed to send deletion confirmation to {Email}", owner.Email);
            }
        }
    }

    private static async Task DeleteCompanyDataAsync(AppDbContext db, short companyId, CancellationToken ct)
    {
        // Delete in FK-safe order (children before parents).
        await db.AppointmentTypeEmployees
            .Where(x => db.AppointmentTypes.Any(t => t.CompanyId == companyId && t.AppointmentTypeId == x.AppointmentTypeId))
            .ExecuteDeleteAsync(ct);
        await db.AppointmentTypes.Where(t => t.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Appointments.Where(a => a.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CallSessions.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CallbackRequests.Where(r => r.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CallBlacklist.Where(b => b.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CompanyDepartments.Where(d => d.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.WhatsAppMessageLogs.Where(l => l.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.EmailLogs.Where(l => l.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.EmailJobs.Where(j => j.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.AuditLogs.Where(l => l.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.IntegrationNotifyRequests.Where(r => r.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.KnowledgeSuggestions.Where(k => k.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.BotActiveHours.Where(h => h.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.AssistantProfiles.Where(p => p.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.AssistantSettings.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.PhoneNumbers.Where(p => p.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CompanyOutlookTokens.Where(t => t.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CompanyOpeningExceptions.Where(e => e.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CompanyOpeningHours.Where(h => h.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.CompanyPackages.Where(p => p.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Employees.Where(e => e.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Companies.Where(c => c.CompanyId == companyId).ExecuteDeleteAsync(ct);
    }
}
