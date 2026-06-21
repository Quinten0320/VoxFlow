namespace AiCallAssistent.Application.DTOs;

public record DashboardStatsDto(
    DateTimeOffset GeneratedAt,
    CallStatsDto Calls,
    AppointmentStatsDto Appointments,
    IReadOnlyList<RecentCallDto> RecentCalls);

public record CallStatsDto(
    int Today,
    int ThisWeek,
    int ThisMonth,
    IReadOnlyList<DayCountDto> Last7Days,
    int OpenCallbacksCount,
    int MissedCallsTodayCount,
    int? AvgDurationSec);

public record DayCountDto(DateOnly Date, int Count);

public record AppointmentStatsDto(
    int Today,
    int ThisWeek,
    IReadOnlyList<UpcomingAppointmentDto> UpcomingToday,
    IReadOnlyList<TypeCountDto> ByTypeThisMonth);

public record UpcomingAppointmentDto(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string Type,
    string DisplayName,
    string EmployeeName);

public record TypeCountDto(
    string Type,
    string DisplayName,
    int Count);

public record RecentCallDto(
    string CallSid,
    string CallerNumber,
    string? CallerName,
    DateTimeOffset StartedAt,
    int? DurationSeconds,
    string? Summary,
    string? CallType,
    string Status);
