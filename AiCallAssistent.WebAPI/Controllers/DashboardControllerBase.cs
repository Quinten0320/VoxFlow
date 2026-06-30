using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AiCallAssistent.WebAPI.Controllers;

[ApiController]
[Authorize]
public abstract class DashboardControllerBase(AppDbContext db) : ControllerBase
{
    /// <summary>Shared database context — used directly by derived controllers.</summary>
    protected AppDbContext Db { get; } = db;

    /// <summary>
    /// Returns 401 if the JWT sub claim is missing/invalid, otherwise null.
    /// Does NOT require an employee record — safe to call during onboarding.
    /// </summary>
    protected IActionResult? EnsureAuthenticated()
    {
        var sub = User.FindFirst("sub")?.Value;
        return sub != null && Guid.TryParse(sub, out _) ? null : Unauthorized();
    }

    /// <summary>
    /// Resolves the company ID by matching the JWT sub claim to employee.auth_user_id.
    /// Also updates last_login_at, email, and tracks new IPs for security alerts.
    /// Returns 401 if the claim is missing, 403 if no active employee is linked.
    /// </summary>
    protected async Task<(short companyId, IActionResult? error)> GetCompanyIdAsync()
    {
        var sub   = User.FindFirst("sub")?.Value;
        var email = User.FindFirst("email")?.Value;

        if (sub == null || !Guid.TryParse(sub, out var authUserId))
            return (0, Unauthorized());

        var employee = await Db.Employees
            .FirstOrDefaultAsync(e => e.AuthUserId == authUserId && e.IsActive);

        if (employee == null)
            return (0, StatusCode(403, new { error = "No active employee record linked to this account" }));

        var emailSender = HttpContext.RequestServices.GetService<EmailSender>();
        var templates   = HttpContext.RequestServices.GetService<EmailTemplateService>();
        await TrackLoginAsync(employee, email, emailSender, templates);

        return (employee.CompanyId, null);
    }

    /// <summary>
    /// Returns 402 if the company's subscription is expired (past_due / canceled past period end).
    /// Call after <see cref="GetCompanyIdAsync"/> on endpoints that should be locked out.
    /// Returns null if the subscription is fine or no package row exists yet (onboarding).
    /// </summary>
    protected async Task<IActionResult?> RequireActiveSubscriptionAsync(short companyId)
    {
        var pkg = await Db.CompanyPackages
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.SubscriptionStatus, p.CurrentPeriodEnd })
            .FirstOrDefaultAsync();

        if (pkg == null) return null;

        var locked = pkg.SubscriptionStatus switch
        {
            "past_due" or "unpaid" => true,
            "canceled" => pkg.CurrentPeriodEnd == null || pkg.CurrentPeriodEnd < DateTimeOffset.UtcNow,
            _ => false,
        };

        return locked
            ? StatusCode(402, new { error = "Abonnement verlopen.", status = pkg.SubscriptionStatus })
            : null;
    }

    /// <summary>
    /// Writes a non-critical audit log entry (actor from JWT email claim).
    /// Call AFTER the main SaveChangesAsync so there are no pending-change conflicts.
    /// </summary>
    protected async Task LogAuditAsync(short companyId, string action)
    {
        try
        {
            var actorEmail = User.FindFirst("email")?.Value ?? "systeem";
            Db.AuditLogs.Add(new AiCallAssistent.Domain.Models.AuditLog
            {
                CompanyId  = companyId,
                ActorEmail = actorEmail,
                Action     = action,
                CreatedAt  = DateTimeOffset.UtcNow,
            });
            await Db.SaveChangesAsync();
        }
        catch { /* audit log is non-critical — never fail the request */ }
    }

    private async Task TrackLoginAsync(Employee employee, string? email,
                                       EmailSender? emailSender, EmailTemplateService? templates)
    {
        var now     = DateTimeOffset.UtcNow;
        var changed = false;

        // Persist email from JWT if we don't have it yet (or it changed)
        if (email != null && employee.Email != email)
        {
            employee.Email = email;
            changed = true;
        }

        // Re-activation: user returning after more than 14 days away
        var wasInactive = employee.LastLoginAt.HasValue
                       && (now - employee.LastLoginAt.Value).TotalDays > 14;

        employee.LastLoginAt = now;
        changed = true;

        // New IP detection
        var ip = (HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                  ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                  ?? "").Split(',')[0].Trim();

        var isNewIp = ip.Length > 0 && !employee.KnownIps.Contains(ip);
        if (isNewIp)
        {
            var ips = employee.KnownIps.TakeLast(4).Append(ip).ToArray();
            employee.KnownIps = ips;
        }

        if (changed)
        {
            try { await Db.SaveChangesAsync(); }
            catch { /* non-critical */ }
        }

        if (emailSender == null || templates == null || employee.Email == null) return;

        var capturedLogger = HttpContext.RequestServices.GetService<ILogger<DashboardControllerBase>>();

        // Send new-login alert (non-blocking)
        if (isNewIp)
        {
            var ua       = HttpContext.Request.Headers["User-Agent"].ToString();
            var device   = ua.Length > 80 ? ua[..80] : ua;
            var tijdstip = now.ToString("dd-MM-yyyy HH:mm");
            var capturedCompanyId = employee.CompanyId;
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = templates.NewLoginDetected(employee.Name, device, ip, tijdstip);
                    await emailSender.SendNowAsync(capturedCompanyId, employee.Email!, employee.Name,
                        "new_login", s, h);
                }
                catch (Exception ex)
                {
                    capturedLogger?.LogError(ex, "Failed to send new_login email for company {CompanyId}", capturedCompanyId);
                }
            });
        }

        // Re-activation email
        if (wasInactive)
        {
            var capturedCompanyId2 = employee.CompanyId;
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = templates.ReActivated(employee.Name);
                    await emailSender.SendNowAsync(capturedCompanyId2, employee.Email!, employee.Name,
                        "re_activated", s, h);
                }
                catch (Exception ex)
                {
                    capturedLogger?.LogError(ex, "Failed to send re_activated email for company {CompanyId}", capturedCompanyId2);
                }
            });
        }
    }
}
