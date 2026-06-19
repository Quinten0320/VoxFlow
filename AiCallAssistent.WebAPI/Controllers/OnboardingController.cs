using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/onboarding")]
public class OnboardingController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Completes the onboarding flow.
    /// Creates the company + owner employee if they don't exist yet,
    /// then upserts assistant_settings and opening_hours.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteOnboardingRequest request)
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub == null || !Guid.TryParse(sub, out var authUserId))
            return Unauthorized();

        // ── 1. Find or create company + owner employee ──────────────────────
        var employee = await db.Employees
            .FirstOrDefaultAsync(e => e.AuthUserId == authUserId);

        short companyId;

        if (employee == null)
        {
            var company = new Company
            {
                CompanyName = request.CompanyName,
                Branch      = request.Branch,
                CompanyInfo = request.Branch,
                IsActive    = true,
                CreatedAt   = DateTimeOffset.UtcNow
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync(); // flush so EF Core populates CompanyId

            companyId = company.CompanyId;

            db.Employees.Add(new Employee
            {
                CompanyId   = companyId,
                AuthUserId  = authUserId,
                Name        = request.CompanyName,
                IsOwner     = true,
                IsActive    = true,
                CreatedAt   = DateTimeOffset.UtcNow
            });
        }
        else
        {
            companyId = employee.CompanyId;
            var company = await db.Companies.FindAsync(companyId);
            if (company != null)
            {
                company.CompanyName = request.CompanyName;
                company.Branch      = request.Branch;
                company.CompanyInfo = request.Branch;
            }
        }

        // ── 2. Upsert assistant settings ─────────────────────────────────────
        var settings = await db.AssistantSettings.FindAsync(companyId);
        if (settings == null)
        {
            settings = new AssistantSettings { CompanyId = companyId };
            db.AssistantSettings.Add(settings);
        }

        settings.Prompt             = request.Prompt ?? string.Empty;
        settings.Language           = "nl";
        settings.GreetingsMessage   = request.GreetingsMessage;
        settings.Tone               = request.Tone;
        settings.CallMode           = request.CallMode ?? "first_line";
        settings.AfterHoursMode     = request.AfterHoursMode;
        settings.RoutingRules       = request.RoutingRules;
        settings.AutoMessageConfig  = request.AutoMessageConfig;
        settings.NotificationConfig = request.NotificationConfig;
        settings.UpdatedAt          = DateTimeOffset.UtcNow;
        settings.AssistantName      = request.AssistantName;
        settings.VoiceKey           = request.VoiceKey;
        settings.WaitTime           = request.WaitTime;
        settings.ForwardNumbers     = request.ForwardNumbers is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(request.ForwardNumbers)
            : null;

        // ── 3. Update escalation number on the existing phone row (if any) ───
        if (!string.IsNullOrWhiteSpace(request.EscalationNumber))
        {
            var phone = await db.PhoneNumbers
                .Where(p => p.CompanyId == companyId)
                .FirstOrDefaultAsync();
            if (phone != null)
                phone.EscalationPhoneNumber = request.EscalationNumber;
        }

        // ── 4. Upsert opening hours ──────────────────────────────────────────
        foreach (var h in request.OpeningHours)
        {
            var row = await db.CompanyOpeningHours
                .Include(x => x.TimeRanges)
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.DayOfWeek == h.DayOfWeek);

            if (row == null)
            {
                row = new CompanyOpeningHour { CompanyId = companyId, DayOfWeek = h.DayOfWeek };
                db.CompanyOpeningHours.Add(row);
            }

            row.IsActive = h.IsActive;

            // Replace time ranges entirely
            db.CompanyOpeningTimeRanges.RemoveRange(row.TimeRanges);
            row.TimeRanges.Clear();

            if (h.IsActive && h.StartTime is { Length: > 0 } && h.EndTime is { Length: > 0 })
            {
                row.TimeRanges.Add(new CompanyOpeningTimeRange
                {
                    StartTime = TimeOnly.Parse(h.StartTime),
                    EndTime   = TimeOnly.Parse(h.EndTime),
                    SortOrder = 0,
                    IsActive  = true
                });
            }
        }

        // ── 5. Upsert additional employees ──────────────────────────────────────
        if (request.Employees is { Count: > 0 })
        {
            foreach (var emp in request.Employees)
            {
                if (string.IsNullOrWhiteSpace(emp.Name)) continue;
                var existing = await db.Employees
                    .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.Name == emp.Name && !e.IsOwner);
                if (existing == null)
                    db.Employees.Add(new Employee
                    {
                        CompanyId = companyId,
                        Name      = emp.Name,
                        Role      = emp.Role,
                        Phone     = emp.Phone,
                        IsOwner   = false,
                        IsActive  = true,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                else
                {
                    existing.Role  = emp.Role;
                    existing.Phone = emp.Phone;
                }
            }
        }

        // ── 6. Upsert opening exceptions (holidays) ──────────────────────────
        if (request.Holidays is { Count: > 0 })
        {
            foreach (var holiday in request.Holidays)
            {
                var start = holiday.Start ?? holiday.End;
                var end   = holiday.End   ?? holiday.Start;
                if (start is null) continue;

                for (var d = start.Value; d <= end!.Value; d = d.AddDays(1))
                {
                    var ex = await db.CompanyOpeningExceptions
                        .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.ExceptionDate == d);
                    if (ex == null)
                    {
                        db.CompanyOpeningExceptions.Add(new CompanyOpeningException
                        {
                            CompanyId     = companyId,
                            ExceptionDate = d,
                            IsClosed      = holiday.IsClosed,
                            IsActive      = true
                        });
                    }
                    else
                    {
                        ex.IsClosed = holiday.IsClosed;
                        ex.IsActive = true;
                    }
                }
            }
        }

        // ── 7. Upsert blacklist entries ──────────────────────────────────────
        if (request.Blacklist is { Count: > 0 })
        {
            foreach (var entry in request.Blacklist)
            {
                if (string.IsNullOrWhiteSpace(entry.PhoneNumber)) continue;
                var exists = await db.CallBlacklist
                    .AnyAsync(b => b.CompanyId == companyId && b.PhoneNumber == entry.PhoneNumber);
                if (!exists)
                    db.CallBlacklist.Add(new CallBlacklist
                    {
                        CompanyId   = companyId,
                        PhoneNumber = entry.PhoneNumber,
                        Reason      = entry.Reason,
                        CreatedAt   = DateTimeOffset.UtcNow
                    });
            }
        }

        // ── 8. Save language / integration requests ──────────────────────────
        if (request.LanguageRequests is { Count: > 0 })
        {
            foreach (var lr in request.LanguageRequests)
            {
                if (string.IsNullOrWhiteSpace(lr.Language)) continue;
                db.CompanyRequests.Add(new AiCallAssistent.Domain.Models.CompanyRequest
                {
                    CompanyId = companyId,
                    Type      = "language",
                    Value     = lr.Language,
                    Note      = lr.Email,
                    Status    = "pending",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        if (request.IntegrationRequests is { Count: > 0 })
        {
            foreach (var ir in request.IntegrationRequests)
            {
                if (string.IsNullOrWhiteSpace(ir.Name)) continue;
                var note = string.Join(" — ", new[] { ir.Email, ir.Note }.Where(s => !string.IsNullOrWhiteSpace(s)));
                db.CompanyRequests.Add(new AiCallAssistent.Domain.Models.CompanyRequest
                {
                    CompanyId = companyId,
                    Type      = "integration",
                    Value     = ir.Name,
                    Note      = string.IsNullOrEmpty(note) ? null : note,
                    Status    = "pending",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { companyId });
    }
}
