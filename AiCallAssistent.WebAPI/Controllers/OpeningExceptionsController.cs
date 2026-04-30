using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/opening-exceptions")]
public class OpeningExceptionsController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>Returns all opening exceptions (special dates) for the company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var exceptions = await Db.CompanyOpeningExceptions
            .Where(e => e.CompanyId == companyId)
            .Include(e => e.TimeRanges)
            .OrderBy(e => e.ExceptionDate)
            .ToListAsync();

        var result = exceptions.Select(e => new OpeningExceptionDto(
            e.ExceptionId,
            e.ExceptionDate,
            e.IsClosed,
            e.IsActive,
            e.TimeRanges
                .OrderBy(r => r.SortOrder)
                .Select(r => new ExceptionTimeRangeDto(
                    r.ExceptionTimeRangeId,
                    r.StartTime.ToString("HH:mm"),
                    r.EndTime.ToString("HH:mm"),
                    r.SortOrder,
                    r.IsActive))
                .ToList()));

        return Ok(result);
    }

    /// <summary>Creates a new opening exception for a specific date.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOpeningExceptionRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var exists = await Db.CompanyOpeningExceptions
            .AnyAsync(e => e.CompanyId == companyId && e.ExceptionDate == request.ExceptionDate);
        if (exists)
            return Conflict(new { error = $"An exception for {request.ExceptionDate} already exists" });

        var exception = new CompanyOpeningException
        {
            CompanyId = companyId,
            ExceptionDate = request.ExceptionDate,
            IsClosed = request.IsClosed,
            IsActive = true
        };

        Db.CompanyOpeningExceptions.Add(exception);
        await Db.SaveChangesAsync();

        foreach (var r in request.TimeRanges)
        {
            if (!TimeOnly.TryParse(r.StartTime, out _) || !TimeOnly.TryParse(r.EndTime, out _))
                return BadRequest(new { error = $"Invalid time format '{r.StartTime}' or '{r.EndTime}'. Use HH:mm." });
        }

        var ranges = request.TimeRanges.Select(r => new CompanyOpeningExceptionTimeRange
        {
            ExceptionId = exception.ExceptionId,
            StartTime = TimeOnly.Parse(r.StartTime),
            EndTime = TimeOnly.Parse(r.EndTime),
            SortOrder = r.SortOrder,
            IsActive = true
        }).ToList();

        Db.CompanyOpeningExceptionTimeRanges.AddRange(ranges);
        await Db.SaveChangesAsync();

        var dto = new OpeningExceptionDto(
            exception.ExceptionId,
            exception.ExceptionDate,
            exception.IsClosed,
            exception.IsActive,
            ranges.Select(r => new ExceptionTimeRangeDto(
                r.ExceptionTimeRangeId,
                r.StartTime.ToString("HH:mm"),
                r.EndTime.ToString("HH:mm"),
                r.SortOrder,
                r.IsActive)).ToList());

        return Created(string.Empty, dto);
    }

    /// <summary>Updates an existing opening exception (replaces its time ranges).</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateOpeningExceptionRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var exception = await Db.CompanyOpeningExceptions
            .FirstOrDefaultAsync(e => e.ExceptionId == id && e.CompanyId == companyId);

        if (exception == null)
            return NotFound();

        exception.IsClosed = request.IsClosed;
        exception.IsActive = request.IsActive;

        await Db.CompanyOpeningExceptionTimeRanges
            .Where(r => r.ExceptionId == id)
            .ExecuteDeleteAsync();

        foreach (var r in request.TimeRanges)
        {
            if (!TimeOnly.TryParse(r.StartTime, out _) || !TimeOnly.TryParse(r.EndTime, out _))
                return BadRequest(new { error = $"Invalid time format '{r.StartTime}' or '{r.EndTime}'. Use HH:mm." });
        }

        var ranges = request.TimeRanges.Select(r => new CompanyOpeningExceptionTimeRange
        {
            ExceptionId = id,
            StartTime = TimeOnly.Parse(r.StartTime),
            EndTime = TimeOnly.Parse(r.EndTime),
            SortOrder = r.SortOrder,
            IsActive = true
        }).ToList();

        Db.CompanyOpeningExceptionTimeRanges.AddRange(ranges);
        await Db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Deletes an opening exception and its time ranges.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.CompanyOpeningExceptions
            .Where(e => e.ExceptionId == id && e.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0)
            return NotFound();

        return NoContent();
    }
}
