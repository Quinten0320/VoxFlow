namespace AiCallAssistent.Application.Services;

public interface IOpeningHoursService
{
    /// <summary>Returns the active time ranges for the given date. Empty list means closed.</summary>
    Task<List<(TimeOnly Start, TimeOnly End)>> GetOpeningRangesForDateAsync(short companyId, DateOnly date);

    /// <summary>Returns true if the company is currently open (Amsterdam local time).</summary>
    Task<bool> IsCompanyOpenAsync(short companyId);
}
