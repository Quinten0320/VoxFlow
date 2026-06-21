using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/invoices")]
public class InvoicesController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var pkg = await Db.CompanyPackages.FirstOrDefaultAsync(p => p.CompanyId == companyId);
        if (pkg?.StripeCustomerId == null)
            return Ok(Array.Empty<InvoiceDto>());

        var invoiceService = new InvoiceService();
        var invoices = await invoiceService.ListAsync(new InvoiceListOptions
        {
            Customer = pkg.StripeCustomerId,
            Limit    = 24,
            Status   = "paid",
        });

        return Ok(invoices.Data.Select(i => new InvoiceDto(
            i.Id,
            i.Created.ToString("yyyy-MM-dd"),
            i.AmountPaid / 100m,
            "Betaald",
            i.InvoicePdf ?? "")));
    }
}
