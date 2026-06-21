using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/appointment-types")]
public class AppointmentTypeController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>Returns all appointment types for the company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var types = await Db.AppointmentTypes
            .Where(t => t.CompanyId == companyId)
            .OrderBy(t => t.DisplayName)
            .Select(t => new AppointmentTypeListDto(
                t.AppointmentTypeId,
                t.Name,
                t.DisplayName,
                t.DurationMinutes,
                t.WaitTime,
                t.IsActive,
                t.AutoTransferEnabled,
                t.AutoTransferDepartmentId))
            .ToListAsync();

        return Ok(types);
    }

    /// <summary>Returns a single appointment type by ID.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var type = await Db.AppointmentTypes
            .Where(t => t.AppointmentTypeId == id && t.CompanyId == companyId)
            .Select(t => new AppointmentTypeListDto(
                t.AppointmentTypeId,
                t.Name,
                t.DisplayName,
                t.DurationMinutes,
                t.WaitTime,
                t.IsActive,
                t.AutoTransferEnabled,
                t.AutoTransferDepartmentId))
            .FirstOrDefaultAsync();

        if (type == null)
            return NotFound();

        return Ok(type);
    }

    /// <summary>Creates a new appointment type for the company.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentTypeRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var type = new AppointmentType
        {
            CompanyId = companyId,
            Name = request.Name,
            DisplayName = request.DisplayName,
            DurationMinutes = request.DurationMinutes,
            WaitTime = request.WaitTime,
            IsActive = true
        };

        Db.AppointmentTypes.Add(type);
        await Db.SaveChangesAsync();

        var dto = new AppointmentTypeListDto(
            type.AppointmentTypeId,
            type.Name,
            type.DisplayName,
            type.DurationMinutes,
            type.WaitTime,
            type.IsActive,
            type.AutoTransferEnabled,
            type.AutoTransferDepartmentId);

        return CreatedAtAction(nameof(GetById), new { id = type.AppointmentTypeId }, dto);
    }

    /// <summary>Updates an existing appointment type.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAppointmentTypeRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.AppointmentTypes
            .Where(t => t.AppointmentTypeId == id && t.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Name, request.Name)
                .SetProperty(t => t.DisplayName, request.DisplayName)
                .SetProperty(t => t.DurationMinutes, request.DurationMinutes)
                .SetProperty(t => t.WaitTime, request.WaitTime)
                .SetProperty(t => t.IsActive, request.IsActive)
                .SetProperty(t => t.AutoTransferEnabled, request.AutoTransferEnabled)
                .SetProperty(t => t.AutoTransferDepartmentId, request.AutoTransferDepartmentId));

        if (rows == 0)
            return NotFound();

        return NoContent();
    }

    /// <summary>Deletes an appointment type.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.AppointmentTypes
            .Where(t => t.AppointmentTypeId == id && t.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0)
            return NotFound();

        return NoContent();
    }
}
