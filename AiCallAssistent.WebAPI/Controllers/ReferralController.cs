using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/referral")]
public class ReferralController(
    AppDbContext db,
    IConfiguration config) : DashboardControllerBase(db)
{
    // GET /api/referral/my-code
    // Returns the company's referral code, generating one if it doesn't exist yet.
    [HttpGet("my-code")]
    public async Task<IActionResult> GetMyCode()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var pkg = await Db.CompanyPackages.FirstOrDefaultAsync(p => p.CompanyId == companyId);
        if (pkg == null) return NotFound(new { error = "Geen pakket gevonden." });

        if (pkg.ReferralCode == null)
        {
            pkg.ReferralCode = Guid.NewGuid().ToString("N")[..8].ToUpper();
            pkg.UpdatedAt = DateTimeOffset.UtcNow;
            await Db.SaveChangesAsync();
        }

        var frontendUrl = config["Frontend:Url"]?.TrimEnd('/') ?? "https://voxflow.nl";

        return Ok(new
        {
            code = pkg.ReferralCode,
            link = $"{frontendUrl}/signup?ref={pkg.ReferralCode}",
        });
    }

    // GET /api/referral/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var referred = await Db.CompanyPackages
            .Where(p => p.ReferredByCompanyId == companyId)
            .ToListAsync();

        var totalReferred  = referred.Count;
        var totalConverted = referred.Count(p => p.SubscriptionStatus == "active");
        var totalRewarded  = referred.Count(p => p.ReferralRewardedAt != null);

        return Ok(new { totalReferred, totalConverted, totalRewarded });
    }
}
