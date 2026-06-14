using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/package")]
public class PackageController(AppDbContext db) : DashboardControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var pkg = await Db.CompanyPackages
            .Where(p => p.CompanyId == companyId)
            .Select(p => new CompanyPackageDto(
                p.MaxCallMinutes,
                p.MaxWhatsAppPerMonth,
                p.FeatureBlacklist,
                p.FeatureCallbackRequests,
                p.FeatureWhatsAppConfirmation,
                p.FeatureWhatsAppReminders,
                p.FeatureDepartmentRouting,
                p.FeatureTransferToHuman,
                p.FeatureAfterHoursMode,
                p.FeatureBranchTools,
                p.UpdatedAt))
            .FirstOrDefaultAsync();

        if (pkg is null)
        {
            // Return the defaults so the frontend always gets a valid object
            var now = DateTimeOffset.UtcNow;
            return Ok(new CompanyPackageDto(
                MaxCallMinutes: null,
                MaxWhatsAppPerMonth: null,
                FeatureBlacklist: true,
                FeatureCallbackRequests: true,
                FeatureWhatsAppConfirmation: true,
                FeatureWhatsAppReminders: true,
                FeatureDepartmentRouting: true,
                FeatureTransferToHuman: true,
                FeatureAfterHoursMode: true,
                FeatureBranchTools: true,
                UpdatedAt: now));
        }

        return Ok(pkg);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] UpsertCompanyPackageRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var pkg = await Db.CompanyPackages.FindAsync(companyId);

        if (pkg is null)
        {
            pkg = new CompanyPackage { CompanyId = companyId };
            Db.CompanyPackages.Add(pkg);
        }

        pkg.MaxCallMinutes             = request.MaxCallMinutes;
        pkg.MaxWhatsAppPerMonth        = request.MaxWhatsAppPerMonth;
        pkg.FeatureBlacklist           = request.FeatureBlacklist;
        pkg.FeatureCallbackRequests    = request.FeatureCallbackRequests;
        pkg.FeatureWhatsAppConfirmation = request.FeatureWhatsAppConfirmation;
        pkg.FeatureWhatsAppReminders   = request.FeatureWhatsAppReminders;
        pkg.FeatureDepartmentRouting   = request.FeatureDepartmentRouting;
        pkg.FeatureTransferToHuman     = request.FeatureTransferToHuman;
        pkg.FeatureAfterHoursMode      = request.FeatureAfterHoursMode;
        pkg.FeatureBranchTools         = request.FeatureBranchTools;
        pkg.UpdatedAt                  = DateTimeOffset.UtcNow;

        await Db.SaveChangesAsync();
        return NoContent();
    }
}
