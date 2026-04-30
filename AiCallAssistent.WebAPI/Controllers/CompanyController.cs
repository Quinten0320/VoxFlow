using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/company")]
public class CompanyController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>Returns the authenticated user's company.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var company = await Db.Companies
            .Where(c => c.CompanyId == companyId)
            .Select(c => new CompanyDto(
                c.CompanyId,
                c.CompanyName,
                c.CompanyType,
                c.CompanyInfo,
                c.PackageType,
                c.IsActive,
                c.CreatedAt))
            .FirstOrDefaultAsync();

        if (company == null)
            return NotFound();

        return Ok(company);
    }

    /// <summary>Updates name, type, and info for the authenticated user's company.</summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateCompanyRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.Companies
            .Where(c => c.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.CompanyName, request.CompanyName)
                .SetProperty(c => c.CompanyType, request.CompanyType)
                .SetProperty(c => c.CompanyInfo, request.CompanyInfo));

        if (rows == 0)
            return NotFound();

        return NoContent();
    }
}
