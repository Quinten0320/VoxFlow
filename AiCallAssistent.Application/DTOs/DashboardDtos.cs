namespace AiCallAssistent.Application.DTOs;

public record CompanyDto(
    short CompanyId,
    string CompanyName,
    short? CompanyType,
    string? CompanyInfo,
    short? PackageType,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record UpdateCompanyRequest(
    string CompanyName,
    short? CompanyType,
    string? CompanyInfo);

public record EmployeeDto(
    long EmployeeId,
    string Name,
    short CompanyId,
    bool IsOwner,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateEmployeeRequest(string Name, bool IsOwner);

public record UpdateEmployeeRequest(string Name, bool IsOwner, bool IsActive);

public record AppointmentTypeListDto(
    long AppointmentTypeId,
    string Name,
    string DisplayName,
    int DurationMinutes,
    bool IsActive);

public record CreateAppointmentTypeRequest(
    string Name,
    string DisplayName,
    int DurationMinutes);

public record UpdateAppointmentTypeRequest(
    string Name,
    string DisplayName,
    int DurationMinutes,
    bool IsActive);

public record AppointmentDto(
    long AppointmentId,
    long EmployeeId,
    string EmployeeName,
    string Type,
    string Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    DateTimeOffset CreatedAt);

public record CreateAppointmentDashboardRequest(
    long EmployeeId,
    string Type,
    string Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);

public record UpdateAppointmentDashboardRequest(
    long EmployeeId,
    string Type,
    string Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);

public record SendReminderRequest(string PhoneNumber);

public record CallSessionDto(
    string CallSid,
    string PhoneNumber,
    string CallerNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Status,
    string? Summary,
    DateTimeOffset CreatedAt);

public record CallSessionPageDto(
    IReadOnlyList<CallSessionDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record OpeningHourDto(
    long OpeningHourId,
    short DayOfWeek,
    bool IsActive,
    IReadOnlyList<TimeRangeDto> TimeRanges);

public record TimeRangeDto(
    long OpeningTimeRangeId,
    string StartTime,
    string EndTime,
    short SortOrder,
    bool IsActive);

public record UpdateOpeningHourRequest(
    bool IsActive,
    IReadOnlyList<UpsertTimeRangeRequest> TimeRanges);

public record UpsertTimeRangeRequest(
    string StartTime,
    string EndTime,
    short SortOrder);

public record OpeningExceptionDto(
    long ExceptionId,
    DateOnly ExceptionDate,
    bool IsClosed,
    bool IsActive,
    IReadOnlyList<ExceptionTimeRangeDto> TimeRanges);

public record ExceptionTimeRangeDto(
    long ExceptionTimeRangeId,
    string StartTime,
    string EndTime,
    short SortOrder,
    bool IsActive);

public record CreateOpeningExceptionRequest(
    DateOnly ExceptionDate,
    bool IsClosed,
    IReadOnlyList<UpsertExceptionTimeRangeRequest> TimeRanges);

public record UpdateOpeningExceptionRequest(
    bool IsClosed,
    bool IsActive,
    IReadOnlyList<UpsertExceptionTimeRangeRequest> TimeRanges);

public record UpsertExceptionTimeRangeRequest(
    string StartTime,
    string EndTime,
    short SortOrder);

public record AssistantSettingsDto(
    long? VoiceId,
    string Prompt,
    string Language,
    string? GreetingsMessage,
    DateTimeOffset? UpdatedAt);

public record UpdateAssistantSettingsRequest(
    long? VoiceId,
    string Prompt,
    string Language,
    string? GreetingsMessage);

public record PhoneNumberDto(
    long PhoneNumberId,
    string AiPhoneNumber,
    string? EscalationPhoneNumber,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreatePhoneNumberRequest(
    string AiPhoneNumber,
    string? EscalationPhoneNumber);

public record UpdatePhoneNumberRequest(
    string? EscalationPhoneNumber,
    bool IsActive);
