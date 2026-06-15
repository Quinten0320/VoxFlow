using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/invoices")]
public class InvoicesController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (_, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        return Ok(Array.Empty<InvoiceDto>());
    }
}
