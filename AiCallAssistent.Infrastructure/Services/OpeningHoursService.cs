using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.Infrastructure.Services;

public class OpeningHoursService(AppDbContext db) : IOpeningHoursService
{
    public async Task<List<(TimeOnly Start, TimeOnly End)>> GetOpeningRangesForDateAsync(short companyId, DateOnly date)
    {
        var exception = await db.CompanyOpeningExceptions
            .Include(e => e.TimeRanges)
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.ExceptionDate == date && e.IsActive);

        if (exception is not null)
        {
            if (exception.IsClosed) return [];
            return exception.TimeRanges
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder)
                .Select(r => (r.StartTime, r.EndTime))
                .ToList();
        }

        var dbDay = ToDbDayOfWeek(date.DayOfWeek);
        var openingHour = await db.CompanyOpeningHours
            .Include(h => h.TimeRanges)
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.DayOfWeek == dbDay && h.IsActive);

        if (openingHour is null) return [];

        return openingHour.TimeRanges
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .Select(r => (r.StartTime, r.EndTime))
            .ToList();
    }

    public async Task<bool> IsCompanyOpenAsync(short companyId)
    {
        var nowNl = NlTimeZone.Now;
        var dateNl = DateOnly.FromDateTime(nowNl.DateTime);
        var timeNl = TimeOnly.FromDateTime(nowNl.DateTime);
        var ranges = await GetOpeningRangesForDateAsync(companyId, dateNl);
        return ranges.Any(r => timeNl >= r.Start && timeNl < r.End);
    }

    // DB convention: 1=Mon, 2=Tue, ..., 7=Sun
    private static short ToDbDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday    => 1,
        DayOfWeek.Tuesday   => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday  => 4,
        DayOfWeek.Friday    => 5,
        DayOfWeek.Saturday  => 6,
        DayOfWeek.Sunday    => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(day))
    };
}
