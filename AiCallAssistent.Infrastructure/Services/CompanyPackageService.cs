using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services;

public class CompanyPackageService(
    AppDbContext db,
    ILogger<CompanyPackageService> logger) : ICompanyPackageService
{
    public async Task<CompanyFeatures> GetFeaturesAsync(short companyId)
    {
        try
        {
            var pkg = await db.CompanyPackages
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .FirstOrDefaultAsync();

            if (pkg is null) return CompanyFeatures.Default;

            return new CompanyFeatures(
                MaxCallMinutes:       pkg.MaxCallMinutes,
                MaxWhatsAppPerMonth:  pkg.MaxWhatsAppPerMonth,
                AllowOverage:         pkg.AllowOverage,
                Blacklist:            pkg.FeatureBlacklist,
                CallbackRequests:     pkg.FeatureCallbackRequests,
                WhatsAppConfirmation: pkg.FeatureWhatsAppConfirmation,
                WhatsAppReminders:    pkg.FeatureWhatsAppReminders,
                DepartmentRouting:    pkg.FeatureDepartmentRouting,
                TransferToHuman:      pkg.FeatureTransferToHuman,
                AfterHoursMode:       pkg.FeatureAfterHoursMode,
                BranchTools:          pkg.FeatureBranchTools);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load package for company {CompanyId} — using defaults", companyId);
            return CompanyFeatures.Default;
        }
    }
}
