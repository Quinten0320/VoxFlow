using System.Globalization;
using System.Text.Json;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services.Email;

public class EmailJobBackgroundService(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    EmailTemplateService templates,
    ILogger<EmailJobBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval    = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DailyWindow = TimeSpan.FromHours(23);

    private DateTimeOffset _lastDailyCheck = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try { await RunAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "EmailJobBackgroundService tick failed"); }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await ProcessDueJobsAsync(ct);

        var now = DateTimeOffset.UtcNow;
        if (now - _lastDailyCheck < DailyWindow) return;
        _lastDailyCheck = now;

        await RunInactivityChecksAsync(now, ct);
        await RunWeeklyReportAsync(now, ct);
        await RunMonthlyReportAsync(now, ct);
    }

    // ── Scheduled job dispatch ────────────────────────────────────────────────

    private async Task ProcessDueJobsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var due = await db.EmailJobs
            .Where(j => j.Status == "pending" && j.ScheduledAt <= DateTimeOffset.UtcNow)
            .OrderBy(j => j.ScheduledAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var job in due)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(job.Payload)
                              ?? [];

                await using var empScope = scopeFactory.CreateAsyncScope();
                var empDb = empScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var employee = await empDb.Employees
                    .Where(e => e.CompanyId == job.CompanyId && e.IsOwner && e.IsActive)
                    .FirstOrDefaultAsync(ct);

                if (employee?.Email == null)
                {
                    job.Status = "cancelled";
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                var (subject, html) = job.EmailType switch
                {
                    "onboarding_start"    => templates.OnboardingStart(employee.Name),
                    "onboarding_reminder" => templates.OnboardingReminder(employee.Name),
                    "account_restricted"  => templates.AccountRestricted(employee.Name,
                                                GetStr(payload, "bedrag")),
                    "winback_1"           => templates.WinBack1(employee.Name,
                                                GetInt(payload, "aantalGesprekken")),
                    "winback_7"           => templates.WinBack7(employee.Name,
                                                GetInt(payload, "kortingsPercentage"),
                                                GetStr(payload, "aanbiedingGeldigTot")),
                    "winback_30"          => templates.WinBack30(employee.Name),
                    _ => throw new InvalidOperationException($"Unknown email type: {job.EmailType}"),
                };

                await emailSender.SendNowAsync(job.CompanyId, employee.Email, employee.Name,
                    job.EmailType, subject, html);

                job.Status = "sent";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process email job {JobId} ({EmailType})", job.Id, job.EmailType);
                job.Status = "cancelled";
            }

            await db.SaveChangesAsync(ct);
        }
    }

    // ── Inactivity checks ─────────────────────────────────────────────────────

    private async Task RunInactivityChecksAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var employees = await db.Employees
            .Where(e => e.IsActive && e.IsOwner && e.LastLoginAt != null && e.Email != null)
            .ToListAsync(ct);

        foreach (var emp in employees)
        {
            var daysSince = (now - emp.LastLoginAt!.Value).TotalDays;
            var companyId = emp.CompanyId;

            if (daysSince >= 30 && !await emailSender.AlreadySentAsync(companyId, "inactive_30", TimeSpan.FromDays(25)))
            {
                var (subj, html) = templates.Inactive30Days(emp.Name);
                await SendToEmployee(emp, companyId, "inactive_30", subj, html);
            }
            else if (daysSince >= 14 && !await emailSender.AlreadySentAsync(companyId, "inactive_14", TimeSpan.FromDays(10)))
            {
                var callCount = await db.CallSessions.CountAsync(c => c.CompanyId == companyId, ct);
                var (subj, html) = templates.Inactive14Days(emp.Name, callCount);
                await SendToEmployee(emp, companyId, "inactive_14", subj, html);
            }
            else if (daysSince >= 7 && !await emailSender.AlreadySentAsync(companyId, "inactive_7", TimeSpan.FromDays(5)))
            {
                var (subj, html) = templates.Inactive7Days(emp.Name);
                await SendToEmployee(emp, companyId, "inactive_7", subj, html);
            }
        }
    }

    // ── Weekly report (every Monday) ──────────────────────────────────────────

    private async Task RunWeeklyReportAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (now.DayOfWeek != DayOfWeek.Monday) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var weekStart = now.AddDays(-7);
        var weekNr    = ISOWeek.GetWeekOfYear(now.DateTime);

        var companies = await db.CompanyPackages
            .Where(p => p.SubscriptionStatus == "active" || p.SubscriptionStatus == "trialing")
            .Select(p => p.CompanyId)
            .ToListAsync(ct);

        foreach (var companyId in companies)
        {
            if (await emailSender.AlreadySentAsync(companyId, "weekly_report", TimeSpan.FromDays(6))) continue;

            var emp = await db.Employees.FirstOrDefaultAsync(
                e => e.CompanyId == companyId && e.IsOwner && e.IsActive && e.Email != null, ct);
            if (emp == null) continue;

            var calls = await db.CallSessions
                .Where(c => c.CompanyId == companyId && c.StartedAt >= weekStart)
                .ToListAsync(ct);

            var gesprekken = calls.Count;
            var avgSeconds = calls.Where(c => c.EndedAt.HasValue)
                                  .Select(c => (c.EndedAt!.Value - c.StartedAt).TotalSeconds)
                                  .DefaultIfEmpty(0).Average();
            var avgDur     = TimeSpan.FromSeconds(avgSeconds).ToString(@"m\:ss");

            var (subj, html) = templates.WeeklyReport(emp.Name, gesprekken, 0, 0, avgDur, weekNr);
            await SendToEmployee(emp, companyId, "weekly_report", subj, html);
        }
    }

    // ── Monthly report (1st of month) ─────────────────────────────────────────

    private async Task RunMonthlyReportAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (now.Day != 1) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var monthStart     = now.AddMonths(-1);
        var prevMonthStart = now.AddMonths(-2);
        var maand          = monthStart.ToString("MMMM yyyy", new CultureInfo("nl-NL"));

        var companies = await db.CompanyPackages
            .Where(p => p.SubscriptionStatus == "active" || p.SubscriptionStatus == "trialing")
            .Select(p => p.CompanyId)
            .ToListAsync(ct);

        foreach (var companyId in companies)
        {
            if (await emailSender.AlreadySentAsync(companyId, "monthly_report", TimeSpan.FromDays(27))) continue;

            var emp = await db.Employees.FirstOrDefaultAsync(
                e => e.CompanyId == companyId && e.IsOwner && e.IsActive && e.Email != null, ct);
            if (emp == null) continue;

            var thisCalls = await db.CallSessions
                .CountAsync(c => c.CompanyId == companyId && c.StartedAt >= monthStart && c.StartedAt < now, ct);
            var prevCalls = await db.CallSessions
                .CountAsync(c => c.CompanyId == companyId && c.StartedAt >= prevMonthStart && c.StartedAt < monthStart, ct);

            var groei = prevCalls == 0 ? "n.v.t." : $"{(thisCalls - prevCalls) * 100 / prevCalls:+0;-0}%";

            var (subj, html) = templates.MonthlyReport(emp.Name, maand, thisCalls, "–", groei, "–");
            await SendToEmployee(emp, companyId, "monthly_report", subj, html);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendToEmployee(Domain.Models.Employee emp, short companyId,
                                      string emailType, string subject, string html)
    {
        if (emp.Email == null) return;
        try
        {
            await emailSender.SendNowAsync(companyId, emp.Email, emp.Name, emailType, subject, html);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send {EmailType} to company {CompanyId}", emailType, companyId);
        }
    }

    private static string GetStr(Dictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var v) ? v.GetString() ?? "" : "";

    private static int GetInt(Dictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var v) && v.TryGetInt32(out var i) ? i : 0;
}
