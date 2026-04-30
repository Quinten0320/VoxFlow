using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services;

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _db;
    private readonly IOutlookCalendarService _outlookCalendar;
    private readonly ILogger<AppointmentService> _logger;

    private readonly record struct EmployeeInfo(long Id, string Name);
    private readonly record struct BookedSlot(long EmployeeId, DateTimeOffset StartTime, DateTimeOffset EndTime);

    public AppointmentService(
        AppDbContext db,
        IOutlookCalendarService outlookCalendar,
        ILogger<AppointmentService> logger)
    {
        _db = db;
        _outlookCalendar = outlookCalendar;
        _logger = logger;
    }

    public async Task<List<AppointmentTypeDto>> GetAppointmentTypesAsync(short companyId)
    {
        return await _db.AppointmentTypes
            .Where(t => t.CompanyId == companyId && t.IsActive)
            .OrderBy(t => t.DisplayName)
            .Select(t => new AppointmentTypeDto
            {
                Name = t.Name,
                DisplayName = t.DisplayName,
                DurationMinutes = t.DurationMinutes
            })
            .ToListAsync();
    }

    public async Task<AppointmentResponse> CreateAppointmentAsync(CreateAppointmentRequest request)
    {
        var typeConfig = await _db.AppointmentTypes
            .FirstOrDefaultAsync(t => t.CompanyId == request.CompanyId && t.Name == request.Type && t.IsActive)
            ?? throw new ArgumentException($"Appointment type '{request.Type}' is not available for this company.");

        var startNl = NlTimeZone.ConvertFromUtc(request.StartTime);
        var dateNl = DateOnly.FromDateTime(startNl.DateTime);
        var timeNl = TimeOnly.FromDateTime(startNl.DateTime);

        var ranges = await GetOpeningRangesAsync(request.CompanyId, dateNl);
        if (ranges.Count == 0)
            throw new ArgumentException("The company is closed on the selected day.");

        var endTime = request.StartTime.AddMinutes(typeConfig.DurationMinutes);
        var endTimeNl = TimeOnly.FromDateTime(NlTimeZone.ConvertFromUtc(endTime).DateTime);

        if (!ranges.Any(r => timeNl >= r.Start && endTimeNl <= r.End))
            throw new ArgumentException("Appointment time is outside opening hours.");

        var startUtc = request.StartTime.ToUniversalTime();
        var endUtc = endTime.ToUniversalTime();

        var employeeId = request.EmployeeId.HasValue
            ? await ValidateEmployeeAsync(request.EmployeeId.Value, request.CompanyId)
            : await FindAvailableEmployeeIdAsync(request.CompanyId, startUtc, endUtc);

        var hasConflict = await _db.Appointments.AnyAsync(a =>
            a.EmployeeId == employeeId &&
            a.StartTime < endUtc &&
            a.EndTime > startUtc);

        if (hasConflict)
            throw new InvalidOperationException("The selected time slot is already booked for this employee.");

        var appointment = new Appointment
        {
            CompanyId = request.CompanyId,
            EmployeeId = employeeId,
            Type = request.Type,
            Description = request.Description,
            StartTime = startUtc,
            EndTime = endUtc,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        var employeeName = await _db.Employees
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => e.Name)
            .FirstAsync();

        var syncToCalendar = await _db.Set<AiCallAssistent.Domain.Models.AssistantSettings>()
            .Where(s => s.CompanyId == request.CompanyId)
            .Select(s => (bool?)s.AppointmentsAutomaticallyToCalendar)
            .FirstOrDefaultAsync() ?? false;

        if (syncToCalendar)
        {
            try
            {
                await _outlookCalendar.CreateEventAsync(
                    request.CompanyId, appointment, employeeName,
                    typeConfig.DisplayName, request.CustomerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to sync appointment {AppointmentId} to Outlook for company {CompanyId}",
                    appointment.AppointmentId, request.CompanyId);
            }
        }

        return new AppointmentResponse
        {
            AppointmentId = appointment.AppointmentId,
            CompanyId = appointment.CompanyId,
            EmployeeId = appointment.EmployeeId,
            EmployeeName = employeeName,
            Type = appointment.Type,
            Description = appointment.Description,
            StartTime = NlTimeZone.ConvertFromUtc(appointment.StartTime),
            EndTime = NlTimeZone.ConvertFromUtc(appointment.EndTime)
        };
    }

    public async Task<AvailabilityResponse> GetAvailabilityAsync(short companyId, string type, DateOnly date)
    {
        var typeConfig = await _db.AppointmentTypes
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Name == type && t.IsActive)
            ?? throw new ArgumentException($"Appointment type '{type}' is not available for this company.");

        var ranges = await GetOpeningRangesAsync(companyId, date);
        if (ranges.Count == 0)
            return new AvailabilityResponse
            {
                CompanyId = companyId, Type = type,
                Date = date.ToString("yyyy-MM-dd"),
                DurationMinutes = typeConfig.DurationMinutes,
                AvailableSlots = []
            };

        var employees = await GetActiveEmployeesAsync(companyId);
        var bookedSlots = await GetBookedSlotsAsync(companyId, date, ranges);
        var availableSlots = new List<TimeSlotResponse>();

        foreach (var employee in employees)
        {
            var employeeSlots = bookedSlots.Where(a => a.EmployeeId == employee.Id).ToList();
            var slots = GenerateSlots(date, ranges, employeeSlots, typeConfig.DurationMinutes);

            availableSlots.AddRange(slots.Select(s => new TimeSlotResponse
            {
                EmployeeId = employee.Id,
                EmployeeName = employee.Name,
                StartTime = s.Start,
                EndTime = s.End
            }));
        }

        return new AvailabilityResponse
        {
            CompanyId = companyId,
            Type = type,
            Date = date.ToString("yyyy-MM-dd"),
            DurationMinutes = typeConfig.DurationMinutes,
            AvailableSlots = availableSlots.OrderBy(s => s.StartTime).ThenBy(s => s.EmployeeName).ToList()
        };
    }

    public async Task<SoonestAvailableResponse> GetSoonestAvailableAsync(short companyId, string type)
    {
        var typeConfig = await _db.AppointmentTypes
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Name == type && t.IsActive)
            ?? throw new ArgumentException($"Appointment type '{type}' is not available for this company.");

        var employees = await GetActiveEmployeesAsync(companyId);
        if (employees.Count == 0)
            return new SoonestAvailableResponse { CompanyId = companyId, Type = type, DurationMinutes = typeConfig.DurationMinutes };

        var nowNl = NlTimeZone.Now;
        var todayNl = DateOnly.FromDateTime(nowNl.DateTime);

        for (var dayOffset = 0; dayOffset < 60; dayOffset++)
        {
            var checkDate = todayNl.AddDays(dayOffset);
            var ranges = await GetOpeningRangesAsync(companyId, checkDate);
            if (ranges.Count == 0) continue;

            var bookedSlots = await GetBookedSlotsAsync(companyId, checkDate, ranges);

            foreach (var employee in employees)
            {
                var employeeSlots = bookedSlots.Where(a => a.EmployeeId == employee.Id).ToList();
                var slots = GenerateSlots(checkDate, ranges, employeeSlots, typeConfig.DurationMinutes);

                var validSlots = dayOffset == 0
                    ? slots.Where(s => s.Start > nowNl).ToList()
                    : slots;

                if (validSlots.Count == 0) continue;

                var first = validSlots[0];
                return new SoonestAvailableResponse
                {
                    CompanyId = companyId,
                    Type = type,
                    DurationMinutes = typeConfig.DurationMinutes,
                    SoonestSlot = new TimeSlotResponse
                    {
                        EmployeeId = employee.Id,
                        EmployeeName = employee.Name,
                        StartTime = first.Start,
                        EndTime = first.End
                    }
                };
            }
        }

        return new SoonestAvailableResponse { CompanyId = companyId, Type = type, DurationMinutes = typeConfig.DurationMinutes };
    }

    private async Task<List<(TimeOnly Start, TimeOnly End)>> GetOpeningRangesAsync(short companyId, DateOnly date)
    {
        // Check for a date-specific exception first (holiday, special hours, etc.)
        var exception = await _db.CompanyOpeningExceptions
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
        var openingHour = await _db.CompanyOpeningHours
            .Include(h => h.TimeRanges)
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.DayOfWeek == dbDay && h.IsActive);

        if (openingHour is null) return [];

        return openingHour.TimeRanges
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .Select(r => (r.StartTime, r.EndTime))
            .ToList();
    }

    /// <summary>
    /// Generates available slots by using range starts and booking end times as candidates.
    /// This handles any gap left by an existing booking without relying on a fixed grid.
    /// </summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> GenerateSlots(
        DateOnly date,
        List<(TimeOnly Start, TimeOnly End)> ranges,
        List<BookedSlot> bookedSlots,
        int durationMinutes)
    {
        var slots = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        foreach (var range in ranges)
        {
            var rangeStart = NlTimeZone.ToDateTimeOffset(date, range.Start);
            var rangeEnd = NlTimeZone.ToDateTimeOffset(date, range.End);

            var candidates = new List<DateTimeOffset> { rangeStart };
            foreach (var booked in bookedSlots)
            {
                if (booked.EndTime > rangeStart && booked.EndTime <= rangeEnd)
                    candidates.Add(booked.EndTime);
            }

            foreach (var candidate in candidates.OrderBy(c => c))
            {
                var slotEnd = candidate.AddMinutes(durationMinutes);
                if (slotEnd > rangeEnd) continue;

                var hasConflict = bookedSlots.Any(b => b.StartTime < slotEnd && b.EndTime > candidate);
                if (!hasConflict)
                    slots.Add((candidate, slotEnd));
            }
        }

        return slots.OrderBy(s => s.Start).ToList();
    }

    private async Task<List<EmployeeInfo>> GetActiveEmployeesAsync(short companyId)
    {
        var rows = await _db.Employees
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .Select(e => new { e.EmployeeId, e.Name })
            .ToListAsync();

        return rows.ConvertAll(r => new EmployeeInfo(r.EmployeeId, r.Name));
    }

    private async Task<List<BookedSlot>> GetBookedSlotsAsync(
        short companyId, DateOnly date, List<(TimeOnly Start, TimeOnly End)> ranges)
    {
        var dateStartUtc = NlTimeZone.ToDateTimeOffset(date, ranges.Min(r => r.Start)).ToUniversalTime();
        var dateEndUtc = NlTimeZone.ToDateTimeOffset(date, ranges.Max(r => r.End)).ToUniversalTime();

        var rows = await _db.Appointments
            .Where(a => a.CompanyId == companyId && a.StartTime < dateEndUtc && a.EndTime > dateStartUtc)
            .Select(a => new { a.EmployeeId, a.StartTime, a.EndTime })
            .ToListAsync();

        return rows.ConvertAll(r => new BookedSlot(r.EmployeeId, r.StartTime, r.EndTime));
    }

    private async Task<long> ValidateEmployeeAsync(long employeeId, short companyId)
    {
        var exists = await _db.Employees
            .AnyAsync(e => e.EmployeeId == employeeId && e.CompanyId == companyId && e.IsActive);

        if (!exists)
            throw new ArgumentException("Employee not found or not active.");

        return employeeId;
    }

    private async Task<long> FindAvailableEmployeeIdAsync(short companyId, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var employeeId = await _db.Employees
            .Where(e => e.CompanyId == companyId && e.IsActive &&
                !_db.Appointments.Any(a =>
                    a.EmployeeId == e.EmployeeId &&
                    a.StartTime < endUtc &&
                    a.EndTime > startUtc))
            .Select(e => (long?)e.EmployeeId)
            .FirstOrDefaultAsync();

        return employeeId ?? throw new InvalidOperationException("No employee is available at the requested time.");
    }

    public async Task<bool> CancelAppointmentAsync(long appointmentId, short companyId)
    {
        var rows = await _db.Appointments
            .Where(a => a.AppointmentId == appointmentId && a.CompanyId == companyId)
            .ExecuteDeleteAsync();

        return rows > 0;
    }

    // DB convention: 1=Mon, 2=Tue, ..., 7=Sun
    private static short ToDbDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday => 4,
        DayOfWeek.Friday => 5,
        DayOfWeek.Saturday => 6,
        DayOfWeek.Sunday => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(day))
    };
}
