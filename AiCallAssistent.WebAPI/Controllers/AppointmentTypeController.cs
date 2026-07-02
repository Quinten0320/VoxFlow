using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/appointment-types")]
public class AppointmentTypeController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var types = await Db.AppointmentTypes
            .Where(t => t.CompanyId == companyId)
            .Include(t => t.AppointmentTypeEmployees)
            .OrderBy(t => t.DisplayName)
            .ToListAsync();

        return Ok(types.Select(ToDto));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var type = await Db.AppointmentTypes
            .Where(t => t.AppointmentTypeId == id && t.CompanyId == companyId)
            .Include(t => t.AppointmentTypeEmployees)
            .FirstOrDefaultAsync();

        if (type == null) return NotFound();

        return Ok(ToDto(type));
    }

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
            Location = request.Location,
            TransferOnRequest = request.TransferOnRequest,
            CallbackOnRequest = request.CallbackOnRequest,
            TransferOutsideHours = request.TransferOutsideHours,
            AfterHoursMode = request.AfterHoursMode,
            UrgentAlwaysForward = request.UrgentAlwaysForward,
            Price = request.Price,
            CancellationPolicy = request.CancellationPolicy,
            AvailableDays = request.AvailableDays,
            AvailableFrom = request.AvailableFrom,
            AvailableTo = request.AvailableTo,
            IsActive = true
        };

        Db.AppointmentTypes.Add(type);
        await Db.SaveChangesAsync();

        await SyncEmployeesAsync(type.AppointmentTypeId, companyId, request.EmployeeIds);

        var created = await Db.AppointmentTypes
            .Where(t => t.AppointmentTypeId == type.AppointmentTypeId)
            .Include(t => t.AppointmentTypeEmployees)
            .FirstAsync();

        return CreatedAtAction(nameof(GetById), new { id = type.AppointmentTypeId }, ToDto(created));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAppointmentTypeRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var type = await Db.AppointmentTypes
            .Where(t => t.AppointmentTypeId == id && t.CompanyId == companyId)
            .FirstOrDefaultAsync();

        if (type == null) return NotFound();

        type.Name = request.Name;
        type.DisplayName = request.DisplayName;
        type.DurationMinutes = request.DurationMinutes;
        type.WaitTime = request.WaitTime;
        type.IsActive = request.IsActive;
        type.TransferOnRequest = request.TransferOnRequest;
        type.CallbackOnRequest = request.CallbackOnRequest;
        type.TransferOutsideHours = request.TransferOutsideHours;
        type.Location = request.Location;
        type.AfterHoursMode = request.AfterHoursMode;
        type.UrgentAlwaysForward = request.UrgentAlwaysForward;
        type.Price = request.Price;
        type.CancellationPolicy = request.CancellationPolicy;
        type.AvailableDays = request.AvailableDays;
        type.AvailableFrom = request.AvailableFrom;
        type.AvailableTo = request.AvailableTo;

        await Db.SaveChangesAsync();
        await SyncEmployeesAsync(id, companyId, request.EmployeeIds);

        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.AppointmentTypes
            .Where(t => t.AppointmentTypeId == id && t.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0) return NotFound();

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AppointmentTypeListDto ToDto(AppointmentType t) => new(
        t.AppointmentTypeId,
        t.Name,
        t.DisplayName,
        t.DurationMinutes,
        t.WaitTime,
        t.IsActive,
        t.TransferOnRequest,
        t.CallbackOnRequest,
        t.TransferOutsideHours,
        t.AppointmentTypeEmployees.Select(e => e.EmployeeId).ToList(),
        t.Location,
        t.AfterHoursMode,
        t.UrgentAlwaysForward,
        t.Price,
        t.CancellationPolicy,
        t.AvailableDays,
        t.AvailableFrom,
        t.AvailableTo);

    private async Task SyncEmployeesAsync(long appointmentTypeId, short companyId, List<long>? requestedIds)
    {
        if (requestedIds == null) return;

        // Verify all requested employee IDs belong to this company
        var validIds = await Db.Employees
            .Where(e => e.CompanyId == companyId && requestedIds.Contains(e.EmployeeId))
            .Select(e => e.EmployeeId)
            .ToListAsync();

        var existing = await Db.AppointmentTypeEmployees
            .Where(x => x.AppointmentTypeId == appointmentTypeId)
            .ToListAsync();

        var toRemove = existing.Where(x => !validIds.Contains(x.EmployeeId)).ToList();
        var toAdd = validIds
            .Where(eid => !existing.Any(x => x.EmployeeId == eid))
            .Select(eid => new AppointmentTypeEmployee { AppointmentTypeId = appointmentTypeId, EmployeeId = eid })
            .ToList();

        if (toRemove.Count > 0) Db.AppointmentTypeEmployees.RemoveRange(toRemove);
        if (toAdd.Count > 0) Db.AppointmentTypeEmployees.AddRange(toAdd);

        if (toRemove.Count > 0 || toAdd.Count > 0)
            await Db.SaveChangesAsync();
    }
}
