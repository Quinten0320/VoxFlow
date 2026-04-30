using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/phone-numbers")]
public class PhoneNumberController(AppDbContext db) : DashboardControllerBase(db)
{
    /// <summary>Returns all phone numbers assigned to the company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var numbers = await Db.PhoneNumbers
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.AiPhoneNumber)
            .Select(p => new PhoneNumberDto(
                p.PhoneNumberId,
                p.AiPhoneNumber,
                p.EscalationPhoneNumber,
                p.IsActive,
                p.CreatedAt))
            .ToListAsync();

        return Ok(numbers);
    }

    /// <summary>Adds a new phone number for the company.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePhoneNumberRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var phoneNumber = new PhoneNumber
        {
            CompanyId = companyId,
            AiPhoneNumber = request.AiPhoneNumber,
            EscalationPhoneNumber = request.EscalationPhoneNumber,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Db.PhoneNumbers.Add(phoneNumber);
        await Db.SaveChangesAsync();

        var dto = new PhoneNumberDto(
            phoneNumber.PhoneNumberId,
            phoneNumber.AiPhoneNumber,
            phoneNumber.EscalationPhoneNumber,
            phoneNumber.IsActive,
            phoneNumber.CreatedAt);

        return Created(string.Empty, dto);
    }

    /// <summary>Updates escalation number and active state.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdatePhoneNumberRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.PhoneNumbers
            .Where(p => p.PhoneNumberId == id && p.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.EscalationPhoneNumber, request.EscalationPhoneNumber)
                .SetProperty(p => p.IsActive, request.IsActive));

        if (rows == 0)
            return NotFound();

        return NoContent();
    }

    /// <summary>Deletes a phone number.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.PhoneNumbers
            .Where(p => p.PhoneNumberId == id && p.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0)
            return NotFound();

        return NoContent();
    }
}
