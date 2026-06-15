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
            ?? throw new ArgumentException($"Afspraaktype '{request.Type}' is niet beschikbaar. Roep get_appointment_types aan om de beschikbare typen op te halen.");

        var startNl = NlTimeZone.ConvertFromUtc(request.StartTime);
        var dateNl = DateOnly.FromDateTime(startNl.DateTime);
        var timeNl = TimeOnly.FromDateTime(startNl.DateTime);

        if (typeConfig.WaitTime > 0)
        {
            var earliestNl = DateOnly.FromDateTime(NlTimeZone.Now.DateTime).AddDays(typeConfig.WaitTime);
            if (dateNl < earliestNl)
                throw new ArgumentException(
                    $"'{typeConfig.DisplayName}' vereist minimaal {typeConfig.WaitTime} dag(en) van tevoren boeken. Vroegst beschikbare datum: {earliestNl:yyyy-MM-dd}.");
        }

        var ranges = await _openingHours.GetOpeningRangesForDateAsync(request.CompanyId, dateNl);
        if (ranges.Count == 0)
            throw new ArgumentException(
                $"Het bedrijf is gesloten op {dateNl:dddd d MMMM}. Roep check_availability aan voor een andere datum om beschikbare tijdsloten te vinden.");

        var endTime = request.StartTime.AddMinutes(typeConfig.DurationMinutes);
        var endTimeNl = TimeOnly.FromDateTime(NlTimeZone.ConvertFromUtc(endTime).DateTime);

        if (!ranges.Any(r => timeNl >= r.Start && endTimeNl <= r.End))
            throw new ArgumentException(
                $"Het tijdstip {timeNl:HH:mm} valt buiten de openingstijden op {dateNl:dddd d MMMM}. Roep check_availability aan voor {dateNl:yyyy-MM-dd} om te zien welke tijdsloten wél beschikbaar zijn.");

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
            throw new InvalidOperationException("Dit tijdstip is al bezet voor deze medewerker. Roep check_availability aan om andere beschikbare tijdsloten te vinden.", ex);
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

    public async Task<AvailabilityResponse> GetAvailabilityAsync(short companyId, string type, DateOnly date,
        string? fromTime = null, string? untilTime = null)
    {
        _logger.LogInformation("GetAvailability: company={CompanyId} type={Type} date={Date} from={From} until={Until}",
            companyId, type, date, fromTime ?? "–", untilTime ?? "–");

        var typeConfig = await _db.AppointmentTypes
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Name == type && t.IsActive)
            ?? throw new ArgumentException($"Afspraaktype '{type}' is niet beschikbaar. Roep get_appointment_types aan voor de juiste naam.");

        var ranges = await _openingHours.GetOpeningRangesForDateAsync(companyId, date);
        if (ranges.Count == 0)
        {
            _logger.LogWarning("GetAvailability: 0 opening ranges for company={CompanyId} date={Date} — returning empty", companyId, date);
            return new AvailabilityResponse
            {
                CompanyId = companyId, Type = type,
                Date = date.ToString("yyyy-MM-dd"),
                DurationMinutes = typeConfig.DurationMinutes,
                AvailableSlots = []
            };
        }

        var employees = await GetActiveEmployeesAsync(companyId);
        _logger.LogInformation("GetAvailability: {EmployeeCount} employee(s), {RangeCount} opening range(s) for {Date}",
            employees.Count, ranges.Count, date);

        var bookedSlots = await GetBookedSlotsAsync(companyId, date, ranges);
        _logger.LogInformation("GetAvailability: {BookedCount} booked slot(s) on {Date}", bookedSlots.Count, date);

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
                EndTime = s.End,
                SpokenTime = ToSpokenDutchTime(s.Start)
            }));
        }

        var ordered = availableSlots.OrderBy(s => s.StartTime).ThenBy(s => s.EmployeeName).ToList();

        if (fromTime is { Length: > 0 } && TimeOnly.TryParse(fromTime, out var fromTo))
            ordered = ordered.Where(s => TimeOnly.FromDateTime(s.StartTime.DateTime) >= fromTo).ToList();
        if (untilTime is { Length: > 0 } && TimeOnly.TryParse(untilTime, out var untilTo))
            ordered = ordered.Where(s => TimeOnly.FromDateTime(s.StartTime.DateTime) < untilTo).ToList();

        // Cap at 5 so the slot list stays manageable for Gemini
        ordered = ordered.Take(5).ToList();

        _logger.LogInformation("GetAvailability: returning {Count} slot(s) for {Date} (after filter/cap)",
            ordered.Count, date);

        return new AvailabilityResponse
        {
            CompanyId = companyId,
            Type = type,
            Date = date.ToString("yyyy-MM-dd"),
            DurationMinutes = typeConfig.DurationMinutes,
            AvailableSlots = ordered
        };
    }

    public async Task<SoonestAvailableResponse> GetSoonestAvailableAsync(short companyId, string type)
    {
        _logger.LogInformation("GetSoonestAvailable: company={CompanyId} type={Type}", companyId, type);

        var typeConfig = await _db.AppointmentTypes
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Name == type && t.IsActive)
            ?? throw new ArgumentException($"Afspraaktype '{type}' is niet beschikbaar. Roep get_appointment_types aan voor de juiste naam.");

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
                        EndTime = earliest.Value.End,
                        SpokenTime = ToSpokenDutchTime(earliest.Value.Start)
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
    /// Generates available slots by walking the opening range in steps of durationMinutes.
    /// Each grid position is checked against existing bookings for conflicts.
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
            var current  = NlTimeZone.ToDateTimeOffset(date, range.Start);
            var rangeEnd = NlTimeZone.ToDateTimeOffset(date, range.End);

            while (true)
            {
                var slotEnd = current.AddMinutes(durationMinutes);
                if (slotEnd > rangeEnd) break;

                var hasConflict = bookedSlots.Any(b => b.StartTime < slotEnd && b.EndTime > current);
                if (!hasConflict)
                    slots.Add((current, slotEnd));

                current = current.AddMinutes(durationMinutes);
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

        return employeeId ?? throw new InvalidOperationException("Er is geen medewerker beschikbaar op dit tijdstip. Roep check_availability aan om beschikbare tijdsloten te vinden.");
    }

    /// <summary>
    /// Converts a DateTimeOffset (with NL offset embedded) to a spoken Dutch time string,
    /// e.g. "om kwart over 2 's middags" or "om twintig voor 9 's ochtends".
    /// </summary>
    private static string ToSpokenDutchTime(DateTimeOffset dt)
    {
        var h = dt.DateTime.Hour;
        var m = dt.DateTime.Minute;

        string period  = h < 12 ? " 's ochtends" : h < 18 ? " 's middags" : " 's avonds";
        int h12     = h > 12 ? h - 12 : h == 0 ? 12 : h;
        int nextH12 = (h + 1) > 12 ? (h + 1) - 12 : h + 1 == 0 ? 12 : h + 1;

        return m switch
        {
            0  => $"om {h12} uur{period}",
            10 => $"om tien over {h12}{period}",
            15 => $"om kwart over {h12}{period}",
            20 => $"om twintig over {h12}{period}",
            30 => $"om half {nextH12}{period}",
            40 => $"om twintig voor {nextH12}{period}",
            45 => $"om kwart voor {nextH12}{period}",
            50 => $"om tien voor {nextH12}{period}",
            _  when m < 30 => $"om {m} over {h12}{period}",
            _              => $"om {60 - m} voor {nextH12}{period}"
        };
    }
}
