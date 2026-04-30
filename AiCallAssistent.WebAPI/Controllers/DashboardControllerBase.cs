using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[ApiController]
[Authorize]
public abstract class DashboardControllerBase(AppDbContext db) : ControllerBase
{
    /// <summary>Shared database context — used directly by derived controllers.</summary>
    protected AppDbContext Db { get; } = db;

    /// <summary>
    /// Resolves the company ID for the authenticated user by matching the JWT sub claim
    /// to employee.auth_user_id. Returns a 401 if the sub claim is missing, or 403 if
    /// no active employee record is linked to this auth user.
    /// </summary>
    protected async Task<(short companyId, IActionResult? error)> GetCompanyIdAsync()
    {
        var sub = User.FindFirst("sub")?.Value;

        if (sub == null || !Guid.TryParse(sub, out var authUserId))
            return (0, Unauthorized());

        var companyId = await Db.Employees
            .Where(e => e.AuthUserId == authUserId && e.IsActive)
            .Select(e => (short?)e.CompanyId)
            .FirstOrDefaultAsync();

        if (companyId == null)
            return (0, StatusCode(403, new
            {
                error = "No active employee record linked to this account",
                hint = "Set auth_user_id on the employee row in Supabase"
            }));

        return (companyId.Value, null);
    }
}
