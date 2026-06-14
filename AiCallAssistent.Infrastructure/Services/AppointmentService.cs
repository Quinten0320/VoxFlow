using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AiCallAssistent.Infrastructure.Services;

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _db;
    private readonly IOpeningHoursService _openingHours;
    private readonly IOutlookCalendarService _outlookCalendar;
    private readonly ILogger<AppointmentService> _logger;

    private readonly record struct EmployeeInfo(long Id, string Name);
    private readonly record struct BookedSlot(long EmployeeId, DateTimeOffset StartTime, DateTimeOffset EndTime);

    public AppointmentService(
        AppDbContext db,
        IOpeningHoursService openingHours,
        IOutlookCalendarService outlookCalendar,
        ILogger<AppointmentService> logger)
    {
        _db = db;
        _openingHours = openingHours;
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
                DurationMinutes = t.DurationMinutes,
                WaitTime = t.WaitTime
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

        if (typeConfig.WaitTime > 0)
        {
            var earliestNl = DateOnly.FromDateTime(NlTimeZone.Now.DateTime).AddDays(typeConfig.WaitTime);
            if (dateNl < earliestNl)
                throw new ArgumentException(
                    $"'{typeConfig.DisplayName}' requires at least {typeConfig.WaitTime} day(s) notice. Earliest available date: {earliestNl:yyyy-MM-dd}.");
        }

        var ranges = await _openingHours.GetOpeningRangesForDateAsync(request.CompanyId, dateNl);
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

        var appointment = new Appointment
        {
            CompanyId = request.CompanyId,
            EmployeeId = employeeId,
            Type = request.Type,
            Description = request.Description,
            StartTime = startUtc,
            EndTime = endUtc,
            CallerPhoneNumber = request.CallerPhoneNumber,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Appointments.Add(appointment);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("The selected time slot is already booked for this employee.", ex);
        }

        var employeeName = await _db.Employees
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => e.Name)
            .FirstAsync();

        var syncToCalendar = await _db.Set<AssistantSettings>()
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

        // Resolve auto-transfer department phone if configured for this appointment type.
        string? autoTransferNumber = null;
        if (typeConfig.AutoTransferEnabled && typeConfig.AutoTransferDepartmentId.HasValue)
        {
            autoTransferNumber = await _db.CompanyDepartments
                .Where(d => d.DepartmentId == typeConfig.AutoTransferDepartmentId.Value
                         && d.CompanyId == request.CompanyId && d.IsActive)
                .Select(d => (string?)d.PhoneNumber)
                .FirstOrDefaultAsync();
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
            EndTime = NlTimeZone.ConvertFromUtc(appointment.EndTime),
            AutoTransferNumber = autoTransferNumber
        };
    }

    public async Task<AvailabilityResponse> GetAvailabilityAsync(short companyId, string type, DateOnly date)
    {
        var typeConfig = await _db.AppointmentTypes
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Name == type && t.IsActive)
            ?? throw new ArgumentException($"Appointment type '{type}' is not available for this company.");

        var ranges = await _openingHours.GetOpeningRangesForDateAsync(companyId, date);
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

        for (var dayOffset = typeConfig.WaitTime; dayOffset < 60 + typeConfig.WaitTime; dayOffset++)
        {
            var checkDate = todayNl.AddDays(dayOffset);
            var ranges = await _openingHours.GetOpeningRangesForDateAsync(companyId, checkDate);
            if (ranges.Count == 0) continue;

            var bookedSlots = await GetBookedSlotsAsync(companyId, checkDate, ranges);

            // Collect the earliest valid slot across ALL employees for this day.
            (DateTimeOffset Start, DateTimeOffset End, EmployeeInfo Employee)? earliest = null;

            foreach (var employee in employees)
            {
                var employeeSlots = bookedSlots.Where(a => a.EmployeeId == employee.Id).ToList();
                var slots = GenerateSlots(checkDate, ranges, employeeSlots, typeConfig.DurationMinutes);

                var validSlots = dayOffset == 0
                    ? slots.Where(s => s.Start > nowNl).ToList()
                    : slots;

                if (validSlots.Count == 0) continue;

                var first = validSlots[0];
                if (earliest is null || first.Start < earliest.Value.Start)
                    earliest = (first.Start, first.End, employee);
            }

            if (earliest is not null)
            {
                return new SoonestAvailableResponse
                {
                    CompanyId = companyId,
                    Type = type,
                    DurationMinutes = typeConfig.DurationMinutes,
                    SoonestSlot = new TimeSlotResponse
                    {
                        EmployeeId = earliest.Value.Employee.Id,
                        EmployeeName = earliest.Value.Employee.Name,
                        StartTime = earliest.Value.Start,
                        EndTime = earliest.Value.End
                    }
                };
            }
        }

        return new SoonestAvailableResponse { CompanyId = companyId, Type = type, DurationMinutes = typeConfig.DurationMinutes };
    }

    public async Task<bool> CancelAppointmentAsync(long appointmentId, short companyId)
    {
        var rows = await _db.Appointments
            .Where(a => a.AppointmentId == appointmentId && a.CompanyId == companyId)
            .ExecuteDeleteAsync();

        return rows > 0;
    }

    /// <summary>
    /// Generates available slots using range-start and booking-end times as candidates.
    /// This correctly handles any gap left by existing bookings without a fixed grid.
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
}
