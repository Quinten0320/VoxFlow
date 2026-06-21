using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.Infrastructure.Services;

public class BotScheduleService(AppDbContext db) : IBotScheduleService
{
    public async Task<bool> IsBotActiveNowAsync(short companyId, int? profileId)
    {
        IQueryable<AiCallAssistent.Domain.Models.BotActiveHour> query = profileId.HasValue
            ? db.BotActiveHours.Where(h => h.ProfileId == profileId.Value)
            : db.BotActiveHours.Where(h => h.CompanyId == companyId && h.ProfileId == null);

        // No windows configured at all → fail open (always active).
        var anyConfigured = await query.AnyAsync();
        if (!anyConfigured) return true;

        var nowNl = NlTimeZone.Now;
        var dayOfWeek = ToDbDayOfWeek(nowNl.DayOfWeek);
        var currentTime = TimeOnly.FromDateTime(nowNl.DateTime);

        var windows = await query
            .Where(h => h.DayOfWeek == dayOfWeek)
            .Select(h => new { h.OpenTime, h.CloseTime })
            .ToListAsync();

        return windows.Any(w => currentTime >= w.OpenTime && currentTime < w.CloseTime);
    }

    // DB convention: 1=Mon … 7=Sun
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
