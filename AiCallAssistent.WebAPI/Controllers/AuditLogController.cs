using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/audit-log")]
public class AuditLogController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 30)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        limit = Math.Clamp(limit, 1, 100);

        var entries = await Db.AuditLogs
            .Where(a => a.CompanyId == companyId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                a.ActorEmail,
                a.Action,
                a.CreatedAt,
            })
            .ToListAsync();

        return Ok(entries);
    }
}
