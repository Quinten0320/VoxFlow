using AiCallAssistent.Application.DTOs;

namespace AiCallAssistent.Application.Services;

public interface ICompanyPackageService
{
    /// <summary>
    /// Returns feature flags and limits for the company.
    /// Falls back to <see cref="CompanyFeatures.Default"/> (all on, unlimited) on missing row or error.
    /// </summary>
    Task<CompanyFeatures> GetFeaturesAsync(short companyId);
}
