using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services;

public class OpeningHoursService(AppDbContext db, ILogger<OpeningHoursService> logger) : IOpeningHoursService
{
    public async Task<List<(TimeOnly Start, TimeOnly End)>> GetOpeningRangesForDateAsync(short companyId, DateOnly date)
    {
        var dbDay = ToDbDayOfWeek(date.DayOfWeek);
        logger.LogInformation("OpeningHours: querying company={CompanyId} date={Date} ({DayOfWeek}, db_day={DbDay})",
            companyId, date, date.DayOfWeek, dbDay);

        var exception = await db.CompanyOpeningExceptions
            .Include(e => e.TimeRanges)
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.ExceptionDate == date && e.IsActive);

        if (exception is not null)
        {
            if (exception.IsClosed)
            {
                logger.LogInformation("OpeningHours: exception overrides day as CLOSED for company={CompanyId} date={Date}", companyId, date);
                return [];
            }
            var exRanges = exception.TimeRanges
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder)
                .Select(r => (r.StartTime, r.EndTime))
                .ToList();
            logger.LogInformation("OpeningHours: exception found — {Count} range(s): {Ranges}",
                exRanges.Count, string.Join(", ", exRanges.Select(r => $"{r.StartTime:HH\\:mm}-{r.EndTime:HH\\:mm}")));
            return exRanges;
        }

        var openingHour = await db.CompanyOpeningHours
            .Include(h => h.TimeRanges)
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.DayOfWeek == dbDay && h.IsActive);

        if (openingHour is null)
        {
            logger.LogWarning("OpeningHours: NO row found for company={CompanyId} db_day={DbDay} (is_active=true). Check that opening hours are saved for this company and day.",
                companyId, dbDay);
            return [];
        }

        var ranges = openingHour.TimeRanges
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .Select(r => (r.StartTime, r.EndTime))
            .ToList();

        logger.LogInformation("OpeningHours: found {Count} range(s) for company={CompanyId} db_day={DbDay}: {Ranges}",
            ranges.Count, companyId, dbDay,
            ranges.Count == 0
                ? "(none — time_ranges may all have is_active=false)"
                : string.Join(", ", ranges.Select(r => $"{r.StartTime:HH\\:mm}-{r.EndTime:HH\\:mm}")));

        return ranges;
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
