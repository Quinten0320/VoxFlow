using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/admin")]
[AllowAnonymous]
public class AdminController(
    AppDbContext db,
    IMemoryCache cache,
    IOptions<AdminSettings> adminOptions,
    EmailSender emailSender,
    EmailTemplateService emailTemplates) : ControllerBase
{
    private const string TokenCachePrefix = "admin_token_";
    private const string TokenHeader = "X-Admin-Token";
    private readonly AdminSettings _admin = adminOptions.Value;

    // ── Auth ─────────────────────────────────────────────────────────────────

    [HttpPost("login")]
    [EnableRateLimiting("strict")]
    public IActionResult Login([FromBody] AdminLoginRequest request)
    {
        if (request.Username != _admin.Username || request.Password != _admin.Password)
            return Unauthorized(new { error = "Ongeldige gebruikersnaam of wachtwoord." });

        var token = Guid.NewGuid().ToString("N");
        cache.Set($"{TokenCachePrefix}{token}", true, TimeSpan.FromHours(8));

        return Ok(new { token });
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        if (!IsAuthorized(out var err)) return err!;

        var totalCompanies  = await db.Companies.CountAsync();
        var activeCompanies = await db.Companies.CountAsync(c => c.IsActive);
        var totalEmployees  = await db.Employees.CountAsync();
        var totalCalls      = await db.CallSessions.CountAsync();
        var callsToday      = await db.CallSessions
            .CountAsync(c => c.StartedAt >= DateTimeOffset.UtcNow.Date);
        var totalCallbacks  = await db.CallbackRequests.CountAsync();

        return Ok(new
        {
            totalCompanies,
            activeCompanies,
            totalEmployees,
            totalCalls,
            callsToday,
            totalCallbacks,
        });
    }

    // ── Companies ─────────────────────────────────────────────────────────────

    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies()
    {
        if (!IsAuthorized(out var err)) return err!;

        var companies = await db.Companies
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.CompanyId,
                c.CompanyName,
                c.Branch,
                c.PackageType,
                c.IsActive,
                c.CreatedAt,
                EmployeeCount = db.Employees.Count(e => e.CompanyId == c.CompanyId),
                CallCount     = db.CallSessions.Count(s => s.CompanyId == c.CompanyId),
                PhoneNumbers  = db.PhoneNumbers
                    .Where(p => p.CompanyId == c.CompanyId)
                    .Select(p => p.AiPhoneNumber)
                    .ToList(),
            })
            .ToListAsync();

        return Ok(companies);
    }

    // ── Calls ─────────────────────────────────────────────────────────────────

    [HttpGet("calls")]
    public async Task<IActionResult> GetCalls([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!IsAuthorized(out var err)) return err!;

        var total = await db.CallSessions.CountAsync();
        var calls = await db.CallSessions
            .OrderByDescending(c => c.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.CallSid,
                c.CompanyId,
                c.CallerNumber,
                c.StartedAt,
                c.EndedAt,
                c.Summary,
                DurationSeconds = c.EndedAt.HasValue
                    ? (int)(c.EndedAt.Value - c.StartedAt).TotalSeconds
                    : (int?)null,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, calls });
    }

    // ── Company detail ────────────────────────────────────────────────────────

    [HttpGet("companies/{id:int}")]
    public async Task<IActionResult> GetCompany(int id)
    {
        if (!IsAuthorized(out var err)) return err!;

        var company = await db.Companies.FindAsync((short)id);
        if (company == null) return NotFound();

        var settings  = await db.AssistantSettings.FindAsync((short)id);
        var package   = await db.CompanyPackages.FindAsync((short)id);
        var outlook   = await db.CompanyOutlookTokens.FindAsync((short)id);
        var phones    = await db.PhoneNumbers
            .Where(p => p.CompanyId == (short)id)
            .Select(p => p.AiPhoneNumber)
            .ToListAsync();

        return Ok(new
        {
            companyId   = company.CompanyId,
            companyName = company.CompanyName,
            branch      = company.Branch,
            packageType = company.PackageType,
            isActive    = company.IsActive,
            createdAt   = company.CreatedAt,
            phoneNumbers = phones,

            assistant = settings == null ? null : new
            {
                language        = settings.Language,
                assistantName   = settings.AssistantName,
                tone            = settings.Tone,
                voiceKey        = settings.VoiceKey,
                callMode        = settings.CallMode,
                afterHoursMode  = settings.AfterHoursMode,
                greetingsMessage = settings.GreetingsMessage,
            },

            features = package == null ? null : new
            {
                blacklist              = package.FeatureBlacklist,
                callbackRequests       = package.FeatureCallbackRequests,
                whatsAppConfirmation   = package.FeatureWhatsAppConfirmation,
                whatsAppReminders      = package.FeatureWhatsAppReminders,
                departmentRouting      = package.FeatureDepartmentRouting,
                transferToHuman        = package.FeatureTransferToHuman,
                afterHoursMode         = package.FeatureAfterHoursMode,
                branchTools            = package.FeatureBranchTools,
                maxCallMinutes         = package.MaxCallMinutes,
                maxWhatsAppPerMonth    = package.MaxWhatsAppPerMonth,
                subscriptionStatus     = package.SubscriptionStatus,
                trialEndsAt            = package.TrialEndsAt,
            },

            integrations = new
            {
                outlookConnected = outlook != null,
                outlookEmail     = outlook?.OutlookEmail,
            },
        });
    }

    [HttpPatch("companies/{id:int}")]
    public async Task<IActionResult> UpdateCompany(int id, [FromBody] AdminUpdateCompanyRequest body)
    {
        if (!IsAuthorized(out var err)) return err!;

        var company = await db.Companies.FindAsync((short)id);
        if (company == null) return NotFound();

        if (body.CompanyName != null) company.CompanyName = body.CompanyName;
        if (body.IsActive.HasValue)   company.IsActive    = body.IsActive.Value;
        if (body.Branch != null)      company.Branch      = body.Branch;

        var settings = await db.AssistantSettings.FindAsync((short)id);
        if (settings != null)
        {
            if (body.Language      != null) settings.Language      = body.Language;
            if (body.AssistantName != null) settings.AssistantName = body.AssistantName;
            if (body.Tone          != null) settings.Tone          = body.Tone;
            if (body.VoiceKey      != null) settings.VoiceKey      = body.VoiceKey;
            if (body.CallMode      != null) settings.CallMode      = body.CallMode;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (body.Features != null)
        {
            var pkg = await db.CompanyPackages.FindAsync((short)id);
            if (pkg != null)
            {
                var f = body.Features;
                if (f.Blacklist.HasValue)             pkg.FeatureBlacklist            = f.Blacklist.Value;
                if (f.CallbackRequests.HasValue)      pkg.FeatureCallbackRequests     = f.CallbackRequests.Value;
                if (f.WhatsAppConfirmation.HasValue)  pkg.FeatureWhatsAppConfirmation = f.WhatsAppConfirmation.Value;
                if (f.WhatsAppReminders.HasValue)     pkg.FeatureWhatsAppReminders    = f.WhatsAppReminders.Value;
                if (f.DepartmentRouting.HasValue)     pkg.FeatureDepartmentRouting    = f.DepartmentRouting.Value;
                if (f.TransferToHuman.HasValue)       pkg.FeatureTransferToHuman      = f.TransferToHuman.Value;
                if (f.AfterHoursMode.HasValue)        pkg.FeatureAfterHoursMode       = f.AfterHoursMode.Value;
                if (f.BranchTools.HasValue)           pkg.FeatureBranchTools          = f.BranchTools.Value;
                if (f.MaxCallMinutes.HasValue)        pkg.MaxCallMinutes              = f.MaxCallMinutes.Value < 0 ? null : f.MaxCallMinutes.Value;
                if (f.MaxWhatsAppPerMonth.HasValue)   pkg.MaxWhatsAppPerMonth         = f.MaxWhatsAppPerMonth.Value < 0 ? null : f.MaxWhatsAppPerMonth.Value;
                pkg.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Requests ──────────────────────────────────────────────────────────────

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests([FromQuery] string? status = null)
    {
        if (!IsAuthorized(out var err)) return err!;

        var q = db.CompanyRequests.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(r => r.Status == status);

        var requests = await q
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.CompanyId, r.Type, r.Value, r.Note, r.Status, r.CreatedAt,
                CompanyName = db.Companies
                    .Where(c => c.CompanyId == r.CompanyId)
                    .Select(c => c.CompanyName)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(requests);
    }

    [HttpPatch("requests/{id:long}/status")]
    public async Task<IActionResult> UpdateRequestStatus(long id, [FromBody] AdminRequestStatusUpdate body)
    {
        if (!IsAuthorized(out var err)) return err!;

        var request = await db.CompanyRequests.FindAsync(id);
        if (request == null) return NotFound();

        request.Status = body.Status;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── WhatsApp ──────────────────────────────────────────────────────────────

    [HttpGet("whatsapp")]
    public async Task<IActionResult> GetWhatsAppRequests()
    {
        if (!IsAuthorized(out var err)) return err!;

        var rows = await db.AssistantSettings
            .Where(s => s.WhatsAppRequested || s.WhatsAppActive)
            .Join(db.Companies, s => s.CompanyId, c => c.CompanyId, (s, c) => new { s, c })
            .GroupJoin(
                db.PhoneNumbers.Where(p => p.IsActive),
                sc => sc.s.CompanyId,
                p => p.CompanyId,
                (sc, phones) => new { sc.s, sc.c, phones })
            .SelectMany(x => x.phones.DefaultIfEmpty(), (x, p) => new AdminWhatsAppRequestDto(
                x.s.CompanyId,
                x.c.CompanyName,
                p != null ? p.AiPhoneNumber : x.s.WhatsAppPhoneNumber,
                x.s.WhatsAppActive,
                x.s.WhatsAppRequestedAt))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPost("companies/{id:int}/whatsapp/activate")]
    public async Task<IActionResult> ActivateWhatsApp(int id)
    {
        if (!IsAuthorized(out var err)) return err!;

        var companyId = (short)id;

        var aiPhoneNumber = await db.PhoneNumbers
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .Select(p => p.AiPhoneNumber)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(aiPhoneNumber))
            return BadRequest(new { error = "Geen actief telefoonnummer gevonden voor dit bedrijf." });

        var settings = await db.AssistantSettings.FindAsync(companyId);
        if (settings == null)
            return NotFound(new { error = "Geen assistent-instellingen gevonden voor dit bedrijf." });

        settings.WhatsAppActive = true;
        settings.WhatsAppPhoneNumber = aiPhoneNumber;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Send confirmation email to company owner
        var owner = await db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.IsOwner && e.IsActive && e.Email != null);

        if (owner?.Email != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var (subject, html) = emailTemplates.WhatsAppActivated(owner.Name, aiPhoneNumber);
                    await emailSender.SendNowAsync(companyId, owner.Email, owner.Name,
                        "whatsapp_activated", subject, html);
                }
                catch { }
            });
        }

        return Ok(new { activated = true, phoneNumber = aiPhoneNumber });
    }

    // ── Integration notify requests ───────────────────────────────────────────

    [HttpGet("integration-requests")]
    public async Task<IActionResult> GetIntegrationRequests()
    {
        if (!IsAuthorized(out var err)) return err!;

        var rows = await db.IntegrationNotifyRequests
            .Where(r => !r.Resolved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.CompanyId,
                r.IntegrationKey,
                r.IntegrationName,
                r.CreatedAt,
                CompanyName = db.Companies
                    .Where(c => c.CompanyId == r.CompanyId)
                    .Select(c => c.CompanyName)
                    .FirstOrDefault(),
                OwnerEmail = db.Employees
                    .Where(e => e.CompanyId == r.CompanyId && e.IsOwner && e.IsActive && e.Email != null)
                    .Select(e => e.Email)
                    .FirstOrDefault(),
                OwnerName = db.Employees
                    .Where(e => e.CompanyId == r.CompanyId && e.IsOwner && e.IsActive)
                    .Select(e => e.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPost("integration-requests/{id:long}/resolve")]
    public async Task<IActionResult> ResolveIntegrationRequest(long id)
    {
        if (!IsAuthorized(out var err)) return err!;

        var request = await db.IntegrationNotifyRequests.FindAsync(id);
        if (request == null) return NotFound();

        request.Resolved = true;
        request.ResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Send "now live" email to owner
        var owner = await db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == request.CompanyId && e.IsOwner && e.IsActive && e.Email != null);

        if (owner?.Email != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var (subject, html) = emailTemplates.IntegrationNowLive(owner.Name, request.IntegrationName);
                    await emailSender.SendNowAsync(request.CompanyId, owner.Email, owner.Name,
                        "integration_now_live", subject, html);
                }
                catch { }
            });
        }

        return Ok(new { resolved = true });
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private bool IsAuthorized(out IActionResult? result)
    {
        if (Request.Headers.TryGetValue(TokenHeader, out var token) &&
            cache.TryGetValue($"{TokenCachePrefix}{token}", out _))
        {
            result = null;
            return true;
        }

        result = Unauthorized(new { error = "Geen geldige admin-sessie." });
        return false;
    }
}

public record AdminLoginRequest(string Username, string Password);
public record AdminFeaturePatch(
    bool? Blacklist, bool? CallbackRequests, bool? WhatsAppConfirmation,
    bool? WhatsAppReminders, bool? DepartmentRouting, bool? TransferToHuman,
    bool? AfterHoursMode, bool? BranchTools,
    int? MaxCallMinutes, int? MaxWhatsAppPerMonth);
public record AdminUpdateCompanyRequest(
    string? CompanyName, bool? IsActive, string? Branch,
    string? Language, string? AssistantName, string? Tone, string? VoiceKey, string? CallMode,
    AdminFeaturePatch? Features);
public record AdminRequestStatusUpdate(string Status);
