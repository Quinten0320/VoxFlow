namespace AiCallAssistent.Application.Helpers;

public static class NlTimeZone
{
    public static readonly TimeZoneInfo Info = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"); }
    }

    public static DateTimeOffset ToDateTimeOffset(DateOnly date, TimeOnly time)
    {
        var dt = new DateTime(date.Year, date.Month, date.Day,
            time.Hour, time.Minute, time.Second, DateTimeKind.Unspecified);
        return new DateTimeOffset(dt, Info.GetUtcOffset(dt));
    }

    public static DateTimeOffset ConvertFromUtc(DateTimeOffset utcTime) =>
        TimeZoneInfo.ConvertTime(utcTime, Info);

    public static DateTimeOffset Now =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Info);
}
