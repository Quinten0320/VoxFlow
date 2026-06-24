using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services;

public class AppointmentNotificationService(
    IServiceScopeFactory scopeFactory,
    ILogger<AppointmentNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "AppointmentNotificationService tick failed"); }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
        var packageService = scope.ServiceProvider.GetRequiredService<ICompanyPackageService>();

        var nowUtc = DateTimeOffset.UtcNow;
        var nowNl = NlTimeZone.Now;

        await SendDayRemindersAsync(db, whatsApp, packageService, nowUtc, nowNl, ct);
        await SendFollowupsAsync(db, whatsApp, packageService, nowUtc, ct);
    }

    private async Task SendDayRemindersAsync(
        AppDbContext db, IWhatsAppService whatsApp, ICompanyPackageService packageService,
        DateTimeOffset nowUtc, DateTimeOffset nowNl, CancellationToken ct)
    {
        // Only send reminders after 08:00 NL time
        if (nowNl.Hour < 8) return;

        var tomorrowStart = new DateTimeOffset(nowUtc.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var tomorrowEnd = tomorrowStart.AddDays(1);

        // Load all candidates in one query, joining employee, company, and appointment type
        var candidates = await db.Appointments
            .Where(a =>
                a.CallerPhoneNumber != null &&
                a.ReminderSentAt == null &&
                a.StartTime >= tomorrowStart &&
                a.StartTime < tomorrowEnd)
            .Select(a => new
            {
                a.AppointmentId,
                a.CompanyId,
                a.Type,
                a.StartTime,
                CallerPhoneNumber = a.CallerPhoneNumber!,
                EmployeeName = a.Employee != null ? a.Employee.Name : string.Empty,
                CompanyName = db.Companies
                    .Where(c => c.CompanyId == a.CompanyId)
                    .Select(c => c.CompanyName)
                    .FirstOrDefault() ?? string.Empty,
                DisplayName = db.AppointmentTypes
                    .Where(t => t.CompanyId == a.CompanyId && t.Name == a.Type)
                    .Select(t => t.DisplayName)
                    .FirstOrDefault() ?? a.Type
            })
            .ToListAsync(ct);

        foreach (var item in candidates)
        {
            try
            {
                var features = await packageService.GetFeaturesAsync(item.CompanyId);
                if (!features.WhatsAppReminders) continue;

                var nl = NlTimeZone.ConvertFromUtc(item.StartTime);
                var msg =
                    $"⏰ Herinnering: morgen heeft u een afspraak bij {item.CompanyName}!\n\n" +
                    $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
                    $"💇 {item.DisplayName}\n" +
                    $"👤 {item.EmployeeName}";

                await whatsApp.SendForCompanyAsync(item.CompanyId, item.CallerPhoneNumber, msg, "reminder");

                await db.Appointments
                    .Where(a => a.AppointmentId == item.AppointmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReminderSentAt, DateTimeOffset.UtcNow), ct);

                logger.LogInformation("Day reminder sent for appointment {AppointmentId}", item.AppointmentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send day reminder for appointment {AppointmentId}", item.AppointmentId);
            }
        }
    }

    private async Task SendFollowupsAsync(
        AppDbContext db, IWhatsAppService whatsApp, ICompanyPackageService packageService,
        DateTimeOffset nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddHours(-2);

        var candidates = await db.Appointments
            .Where(a =>
                a.CallerPhoneNumber != null &&
                a.FollowupSentAt == null &&
                a.EndTime <= cutoff)
            .Select(a => new
            {
                a.AppointmentId,
                a.CompanyId,
                a.Type,
                CallerPhoneNumber = a.CallerPhoneNumber!,
                CompanyName = db.Companies
                    .Where(c => c.CompanyId == a.CompanyId)
                    .Select(c => c.CompanyName)
                    .FirstOrDefault() ?? string.Empty,
                DisplayName = db.AppointmentTypes
                    .Where(t => t.CompanyId == a.CompanyId && t.Name == a.Type)
                    .Select(t => t.DisplayName)
                    .FirstOrDefault() ?? a.Type
            })
            .ToListAsync(ct);

        foreach (var item in candidates)
        {
            try
            {
                var features = await packageService.GetFeaturesAsync(item.CompanyId);
                if (!features.WhatsAppReminders) continue;

                var msg =
                    $"😊 Bedankt voor uw bezoek bij {item.CompanyName}!\n\n" +
                    $"We hopen dat uw {item.DisplayName} naar wens was. Tot de volgende keer!";

                await whatsApp.SendForCompanyAsync(item.CompanyId, item.CallerPhoneNumber, msg, "followup");

                await db.Appointments
                    .Where(a => a.AppointmentId == item.AppointmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.FollowupSentAt, DateTimeOffset.UtcNow), ct);

                logger.LogInformation("Follow-up sent for appointment {AppointmentId}", item.AppointmentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send follow-up for appointment {AppointmentId}", item.AppointmentId);
            }
        }
    }
}
