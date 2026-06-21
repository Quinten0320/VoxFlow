using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/departments")]
public class DepartmentController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var departments = await Db.CompanyDepartments
            .Where(d => d.CompanyId == companyId)
            .OrderBy(d => d.DisplayName)
            .Select(d => new DepartmentListDto(
                d.DepartmentId,
                d.Name,
                d.DisplayName,
                d.PhoneNumber,
                d.IsActive))
            .ToListAsync();

        return Ok(departments);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var dept = await Db.CompanyDepartments
            .Where(d => d.DepartmentId == id && d.CompanyId == companyId)
            .Select(d => new DepartmentListDto(
                d.DepartmentId,
                d.Name,
                d.DisplayName,
                d.PhoneNumber,
                d.IsActive))
            .FirstOrDefaultAsync();

        if (dept == null) return NotFound();
        return Ok(dept);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var dept = new CompanyDepartment
        {
            CompanyId = companyId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Db.CompanyDepartments.Add(dept);
        await Db.SaveChangesAsync();

        var dto = new DepartmentListDto(
            dept.DepartmentId,
            dept.Name,
            dept.DisplayName,
            dept.PhoneNumber,
            dept.IsActive);

        return CreatedAtAction(nameof(GetById), new { id = dept.DepartmentId }, dto);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateDepartmentRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.CompanyDepartments
            .Where(d => d.DepartmentId == id && d.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Name, request.Name)
                .SetProperty(d => d.DisplayName, request.DisplayName)
                .SetProperty(d => d.PhoneNumber, request.PhoneNumber)
                .SetProperty(d => d.IsActive, request.IsActive));

        if (rows == 0) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.CompanyDepartments
            .Where(d => d.DepartmentId == id && d.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0) return NotFound();
        return NoContent();
    }
}
