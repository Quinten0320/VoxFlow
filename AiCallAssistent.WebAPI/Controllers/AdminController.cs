using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/admin")]
[AllowAnonymous]
public class AdminController(
    AppDbContext db,
    IMemoryCache cache,
    IOptions<AdminSettings> adminOptions) : ControllerBase
{
    private const string TokenCachePrefix = "admin_token_";
    private const string TokenHeader = "X-Admin-Token";
    private readonly AdminSettings _admin = adminOptions.Value;

    // ── Auth ─────────────────────────────────────────────────────────────────

    [HttpPost("login")]
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
