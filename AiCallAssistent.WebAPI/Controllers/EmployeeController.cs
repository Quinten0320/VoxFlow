using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/employees")]
public class EmployeeController(AppDbContext db, EmailSender emailSender, EmailTemplateService templates,
    ILogger<EmployeeController> logger)
    : DashboardControllerBase(db)
{
    /// <summary>Returns all employees for the company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var employees = await Db.Employees
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeDto(
                e.EmployeeId,
                e.Name,
                e.CompanyId,
                e.IsOwner,
                e.IsActive,
                e.CreatedAt))
            .ToListAsync();

        return Ok(employees);
    }

    /// <summary>Returns a single employee by ID (must belong to the company).</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var employee = await Db.Employees
            .Where(e => e.EmployeeId == id && e.CompanyId == companyId)
            .Select(e => new EmployeeDto(
                e.EmployeeId,
                e.Name,
                e.CompanyId,
                e.IsOwner,
                e.IsActive,
                e.CreatedAt))
            .FirstOrDefaultAsync();

        if (employee == null)
            return NotFound();

        return Ok(employee);
    }

    /// <summary>Creates a new employee for the company.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var employee = new Employee
        {
            Name = request.Name,
            CompanyId = companyId,
            IsOwner = request.IsOwner,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Db.Employees.Add(employee);
        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, $"Medewerker toegevoegd: {employee.Name}");

        var dto = new EmployeeDto(
            employee.EmployeeId,
            employee.Name,
            employee.CompanyId,
            employee.IsOwner,
            employee.IsActive,
            employee.CreatedAt);

        return CreatedAtAction(nameof(GetById), new { id = employee.EmployeeId }, dto);
    }

    /// <summary>Updates an existing employee.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateEmployeeRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.Employees
            .Where(e => e.EmployeeId == id && e.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Name, request.Name)
                .SetProperty(e => e.IsOwner, request.IsOwner)
                .SetProperty(e => e.IsActive, request.IsActive));

        if (rows == 0)
            return NotFound();

        await LogAuditAsync(companyId, $"Medewerker bijgewerkt: {request.Name}");
        return NoContent();
    }

    /// <summary>Deletes an employee (hard delete).</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var employee = await Db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == id && e.CompanyId == companyId);
        if (employee == null) return NotFound();

        Db.Employees.Remove(employee);
        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, $"Medewerker verwijderd: {employee.Name}");

        if (employee.Email != null)
        {
            var company = await Db.Companies.FindAsync(companyId);
            var capturedCompanyId = companyId;
            var capturedEmail = employee.Email;
            var capturedName = employee.Name;
            var capturedCompanyName = company?.CompanyName ?? "VoxFlow";
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = templates.TeamMemberRemoved(capturedName, capturedCompanyName);
                    await emailSender.SendNowAsync(capturedCompanyId, capturedEmail, capturedName,
                        "team_member_removed", s, h);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send team_member_removed email for company {CompanyId}", capturedCompanyId);
                }
            });
        }

        return NoContent();
    }
}
