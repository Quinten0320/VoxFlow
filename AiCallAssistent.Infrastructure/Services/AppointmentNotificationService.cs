using System.Text.Json;
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
        await SendDayOfRemindersAsync(db, whatsApp, packageService, nowUtc, nowNl, ct);
        await SendFollowupsAsync(db, whatsApp, packageService, nowUtc, ct);
    }

    private static Dictionary<string, bool> ParseAutoMessageFlags(string? json)
    {
        if (json is not { Length: > 0 }) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? []; }
        catch { return []; }
    }

    private static async Task<Dictionary<short, Dictionary<string, bool>>> LoadAutoMessageFlagsAsync(
        AppDbContext db, IEnumerable<short> companyIds, CancellationToken ct)
    {
        var ids = companyIds.Distinct().ToList();
        var rows = await db.AssistantSettings
            .AsNoTracking()
            .Where(s => ids.Contains(s.CompanyId))
            .Select(s => new { s.CompanyId, s.AutoMessageConfig })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.CompanyId, r => ParseAutoMessageFlags(r.AutoMessageConfig));
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
        var candidates = await (
            from a in db.Appointments
            join c in db.Companies on a.CompanyId equals c.CompanyId into cj
            from c in cj.DefaultIfEmpty()
            join t in db.AppointmentTypes on new { a.CompanyId, Name = a.Type } equals new { t.CompanyId, t.Name } into tj
            from t in tj.DefaultIfEmpty()
            where a.CallerPhoneNumber != null
               && a.ReminderSentAt == null
               && a.StartTime >= tomorrowStart
               && a.StartTime < tomorrowEnd
            select new
            {
                a.AppointmentId,
                a.CompanyId,
                a.Type,
                a.StartTime,
                CallerPhoneNumber = a.CallerPhoneNumber!,
                EmployeeName = a.Employee != null ? a.Employee!.Name ?? string.Empty : string.Empty,
                CompanyName = c != null ? c.CompanyName : string.Empty,
                DisplayName = t != null ? t.DisplayName : a.Type,
            }
        ).ToListAsync(ct);

        var autoFlagsByCompany = await LoadAutoMessageFlagsAsync(db, candidates.Select(c => c.CompanyId), ct);

        foreach (var item in candidates)
        {
            try
            {
                var features = await packageService.GetFeaturesAsync(item.CompanyId);
                if (!features.WhatsAppReminders) continue;

                var flags = autoFlagsByCompany.GetValueOrDefault(item.CompanyId, []);
                if (!flags.GetValueOrDefault("appointment_reminder", true)) continue;

                await whatsApp.SendAppointmentReminderAsync(
                    item.CompanyId, item.CallerPhoneNumber,
                    item.CompanyName, null, item.StartTime, item.DisplayName);

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

    private async Task SendDayOfRemindersAsync(
        AppDbContext db, IWhatsAppService whatsApp, ICompanyPackageService packageService,
        DateTimeOffset nowUtc, DateTimeOffset nowNl, CancellationToken ct)
    {
        // Only send after 07:00 NL time
        if (nowNl.Hour < 7) return;

        var todayStart = new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero);
        var todayEnd   = todayStart.AddDays(1);

        var candidates = await (
            from a in db.Appointments
            join c in db.Companies on a.CompanyId equals c.CompanyId into cj
            from c in cj.DefaultIfEmpty()
            join t in db.AppointmentTypes on new { a.CompanyId, Name = a.Type } equals new { t.CompanyId, t.Name } into tj
            from t in tj.DefaultIfEmpty()
            where a.CallerPhoneNumber != null
               && a.DayOfReminderSentAt == null
               && a.StartTime >= nowUtc          // not yet started
               && a.StartTime >= todayStart
               && a.StartTime < todayEnd
            select new
            {
                a.AppointmentId,
                a.CompanyId,
                a.Type,
                a.StartTime,
                CallerPhoneNumber = a.CallerPhoneNumber!,
                CompanyName = c != null ? c.CompanyName : string.Empty,
                DisplayName = t != null ? t.DisplayName : a.Type,
            }
        ).ToListAsync(ct);

        var autoFlagsByCompany = await LoadAutoMessageFlagsAsync(db, candidates.Select(c => c.CompanyId), ct);

        foreach (var item in candidates)
        {
            try
            {
                var features = await packageService.GetFeaturesAsync(item.CompanyId);
                if (!features.WhatsAppReminders) continue;

                var flags = autoFlagsByCompany.GetValueOrDefault(item.CompanyId, []);
                if (!flags.GetValueOrDefault("appointment_day_reminder", true)) continue;

                await whatsApp.SendAppointmentDayReminderAsync(
                    item.CompanyId, item.CallerPhoneNumber,
                    item.CompanyName, null, item.StartTime, item.DisplayName);

                await db.Appointments
                    .Where(a => a.AppointmentId == item.AppointmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.DayOfReminderSentAt, DateTimeOffset.UtcNow), ct);

                logger.LogInformation("Day-of reminder sent for appointment {AppointmentId}", item.AppointmentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send day-of reminder for appointment {AppointmentId}", item.AppointmentId);
            }
        }
    }

    private async Task SendFollowupsAsync(
        AppDbContext db, IWhatsAppService whatsApp, ICompanyPackageService packageService,
        DateTimeOffset nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddHours(-2);

        var candidates = await (
            from a in db.Appointments
            join c in db.Companies on a.CompanyId equals c.CompanyId into cj
            from c in cj.DefaultIfEmpty()
            join t in db.AppointmentTypes on new { a.CompanyId, Name = a.Type } equals new { t.CompanyId, t.Name } into tj
            from t in tj.DefaultIfEmpty()
            where a.CallerPhoneNumber != null
               && a.FollowupSentAt == null
               && a.EndTime <= cutoff
            select new
            {
                a.AppointmentId,
                a.CompanyId,
                a.Type,
                CallerPhoneNumber = a.CallerPhoneNumber!,
                CompanyName = c != null ? c.CompanyName : string.Empty,
                DisplayName = t != null ? t.DisplayName : a.Type,
            }
        ).ToListAsync(ct);

        var autoFlagsByCompany = await LoadAutoMessageFlagsAsync(db, candidates.Select(c => c.CompanyId), ct);

        foreach (var item in candidates)
        {
            try
            {
                var features = await packageService.GetFeaturesAsync(item.CompanyId);
                if (!features.WhatsAppReminders) continue;

                var flags = autoFlagsByCompany.GetValueOrDefault(item.CompanyId, []);
                if (!flags.GetValueOrDefault("appointment_followup", true)) continue;

                await whatsApp.SendAppointmentFollowupAsync(
                    item.CompanyId, item.CallerPhoneNumber,
                    item.CompanyName, null);

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
