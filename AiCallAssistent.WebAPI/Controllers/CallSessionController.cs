using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/call-sessions")]
public class CallSessionController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>
    /// Returns paginated call sessions for the company.
    /// Query params: page (default 1), pageSize (default 20, max 100), classification (optional filter).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? classification = null)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var baseQuery = Db.CallSessions.Where(s => s.CompanyId == companyId);

        if (classification is { Length: > 0 })
            baseQuery = baseQuery.Where(s => s.CallerClassification == classification);

        var totalCount = await baseQuery.CountAsync();

        var items = await baseQuery
            .OrderByDescending(s => s.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new CallSessionDto(
                s.CallSid,
                s.PhoneNumber,
                s.CallerNumber,
                s.StartedAt,
                s.EndedAt,
                s.Status,
                s.Summary,
                s.CallerClassification,
                s.CreatedAt))
            .ToListAsync();

        return Ok(new CallSessionPageDto(items, totalCount, page, pageSize));
    }

    /// <summary>Returns a single call session by call SID.</summary>
    [HttpGet("{callSid}")]
    public async Task<IActionResult> GetById(string callSid)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var session = await Db.CallSessions
            .Where(s => s.CallSid == callSid && s.CompanyId == companyId)
            .Select(s => new CallSessionDto(
                s.CallSid,
                s.PhoneNumber,
                s.CallerNumber,
                s.StartedAt,
                s.EndedAt,
                s.Status,
                s.Summary,
                s.CallerClassification,
                s.CreatedAt))
            .FirstOrDefaultAsync();

        if (session == null)
            return NotFound();

        return Ok(session);
    }
}
