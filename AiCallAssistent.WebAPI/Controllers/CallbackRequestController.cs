using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/callback-requests")]
public class CallbackRequestController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status = null)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var query = Db.CallbackRequests
            .Where(c => c.CompanyId == companyId);

        if (status is { Length: > 0 })
            query = query.Where(c => c.Status == status);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CallbackRequestDto(
                c.CallbackRequestId,
                c.CallerNumber,
                c.CallerName,
                c.Reason,
                c.ScheduledFrom,
                c.ScheduledUntil,
                c.Status,
                c.CreatedAt))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPatch("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateCallbackStatusRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.CallbackRequests
            .Where(c => c.CallbackRequestId == id && c.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, request.Status));

        if (rows == 0) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.CallbackRequests
            .Where(c => c.CallbackRequestId == id && c.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0) return NotFound();
        return NoContent();
    }
}
