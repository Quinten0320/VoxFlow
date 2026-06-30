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
    short WaitTime,
    bool IsActive,
    bool TransferOnRequest,
    bool CallbackOnRequest,
    bool TransferOutsideHours,
    List<long> EmployeeIds,
    string? Location);

public record CreateAppointmentTypeRequest(
    string Name,
    string DisplayName,
    int DurationMinutes,
    short WaitTime = 0,
    List<long>? EmployeeIds = null,
    string? Location = null,
    bool TransferOnRequest = false,
    bool CallbackOnRequest = false,
    bool TransferOutsideHours = false);

public record UpdateAppointmentTypeRequest(
    string Name,
    string DisplayName,
    int DurationMinutes,
    short WaitTime,
    bool IsActive,
    bool TransferOnRequest = false,
    bool CallbackOnRequest = false,
    bool TransferOutsideHours = false,
    List<long>? EmployeeIds = null,
    string? Location = null);

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
    string? CallerName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int? DurationSeconds,
    string Status,
    string? CallType,
    string? Summary,
    string? Transcript,
    string? CallerClassification,
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
    bool AppointmentsAutomaticallyToCalendar,
    string? AfterHoursMode,
    string CallMode,
    bool BotActiveHoursEnabled,
    DateTimeOffset? UpdatedAt,
    string? Tone,
    string? RoutingRules,
    string? AutoMessageConfig,
    string? NotificationConfig,
    string? AssistantName,
    bool AutoTimeGreeting,
    bool UseCallerName,
    string? TopicsYes,
    string? TopicsNo,
    string? FallbackBehavior,
    string? BehaviorInstructions);

public record UpdateAssistantSettingsRequest(
    long? VoiceId,
    string Prompt,
    string Language,
    string? GreetingsMessage,
    bool AppointmentsAutomaticallyToCalendar,
    string? AfterHoursMode,
    string CallMode = "first_line",
    bool BotActiveHoursEnabled = false,
    string? Tone = null,
    string? RoutingRules = null,
    string? AutoMessageConfig = null,
    string? NotificationConfig = null,
    string? AssistantName = null,
    bool AutoTimeGreeting = false,
    bool UseCallerName = false,
    string? TopicsYes = null,
    string? TopicsNo = null,
    string? FallbackBehavior = null,
    string? BehaviorInstructions = null);

public record WhatsAppConnectRequest(string PhoneNumber);

public record WhatsAppStatusDto(bool Requested, bool Active, string? PhoneNumber);

public record AdminWhatsAppRequestDto(
    short CompanyId,
    string CompanyName,
    string? AiPhoneNumber,
    bool Active,
    DateTimeOffset? RequestedAt);

public record PhoneNumberDto(
    long PhoneNumberId,
    string AiPhoneNumber,
    string? EscalationPhoneNumber,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record DepartmentListDto(
    long DepartmentId,
    string Name,
    string DisplayName,
    string PhoneNumber,
    bool IsActive);

public record CreateDepartmentRequest(
    string Name,
    string DisplayName,
    string PhoneNumber);

public record UpdateDepartmentRequest(
    string Name,
    string DisplayName,
    string PhoneNumber,
    bool IsActive);

public record CreatePhoneNumberRequest(
    string AiPhoneNumber,
    string? EscalationPhoneNumber);

public record UpdatePhoneNumberRequest(
    string? EscalationPhoneNumber,
    bool IsActive);

public record AvailablePhoneNumberDto(string PhoneNumber, string FriendlyName, bool IsOwned);

public record PurchasePhoneNumberRequest(string PhoneNumber, bool IsOwned = false);

public record BlacklistEntryDto(
    long BlacklistId,
    string PhoneNumber,
    string? Reason,
    DateTimeOffset CreatedAt);

public record CreateBlacklistEntryRequest(
    string PhoneNumber,
    string? Reason);

public record CallbackRequestDto(
    long CallbackRequestId,
    string CallerNumber,
    string CallerName,
    string Reason,
    DateTimeOffset ScheduledFrom,
    DateTimeOffset ScheduledUntil,
    string Status,
    DateTimeOffset CreatedAt,
    string Priority,
    bool PossibleDuplicate);

public record UpdateCallbackStatusRequest(string Status);

public record CompanyPackageDto(
    int? MaxCallMinutes,
    int? MaxWhatsAppPerMonth,
    bool AllowOverage,
    bool FeatureBlacklist,
    bool FeatureCallbackRequests,
    bool FeatureWhatsAppConfirmation,
    bool FeatureWhatsAppReminders,
    bool FeatureDepartmentRouting,
    bool FeatureTransferToHuman,
    bool FeatureAfterHoursMode,
    bool FeatureBranchTools,
    DateTimeOffset UpdatedAt);

public record UpsertCompanyPackageRequest(
    int? MaxCallMinutes,
    int? MaxWhatsAppPerMonth,
    bool AllowOverage,
    bool FeatureBlacklist,
    bool FeatureCallbackRequests,
    bool FeatureWhatsAppConfirmation,
    bool FeatureWhatsAppReminders,
    bool FeatureDepartmentRouting,
    bool FeatureTransferToHuman,
    bool FeatureAfterHoursMode,
    bool FeatureBranchTools);

// ── Assistant Profiles ───────────────────────────────────────────────────────

public record AssistantProfileDto(
    int Id,
    short CompanyId,
    string Name,
    bool IsActive,
    string? SystemPrompt,
    string? GreetingMessage,
    string Language,
    string CallMode,
    string? AfterHoursMode,
    bool BotActiveHoursEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateAssistantProfileRequest(
    string Name,
    string? SystemPrompt,
    string? GreetingMessage,
    string Language = "nl",
    string CallMode = "first_line",
    string? AfterHoursMode = null,
    bool BotActiveHoursEnabled = false);

public record UpdateAssistantProfileRequest(
    string Name,
    string? SystemPrompt,
    string? GreetingMessage,
    string Language,
    string CallMode,
    string? AfterHoursMode,
    bool BotActiveHoursEnabled);

// ── Bot Active Hours ─────────────────────────────────────────────────────────

public record BotActiveHourDto(
    int Id,
    short DayOfWeek,
    string OpenTime,
    string CloseTime,
    int? ProfileId);

public record UpsertBotActiveHourRequest(
    short DayOfWeek,
    string OpenTime,
    string CloseTime,
    int? ProfileId = null);

// ── Knowledge Suggestions ────────────────────────────────────────────────────

public record KnowledgeSuggestionDto(
    long Id,
    string Text,
    string Status,
    DateTimeOffset CreatedAt);

public record UpdateKnowledgeSuggestionRequest(string Status);

// ── Reporting ────────────────────────────────────────────────────────────────

public record ReportingDto(
    IReadOnlyList<DayCallCountDto>   CallsPerDay,
    IReadOnlyList<CallTypeCountDto>  CallTypeDistribution,
    IReadOnlyList<PeakHeatmapDayDto> PeakHeatmap,
    int? AvgDurationSec,
    int  FirstCallResolutionPct,
    int  CallbackOpen,
    int  CallbackHandled);

public record DayCallCountDto(string Day, int Aantal);
public record CallTypeCountDto(string Name, int Value);
public record PeakHeatmapDayDto(string Day, IReadOnlyList<PeakHeatmapCellDto> Cells);
public record PeakHeatmapCellDto(int Hour, int Value);

// ── Onboarding ───────────────────────────────────────────────────────────────

public record CompleteOnboardingRequest(
    string CompanyName,
    string? Branch,
    string? AssistantName,
    string? VoiceKey,
    string? Prompt,
    string? GreetingsMessage,
    string? Tone,
    string? WaitTime,
    string? CallMode,
    string? AfterHoursMode,
    string? EscalationNumber,
    IReadOnlyList<string>?                          ForwardNumbers,
    string? RoutingRules,
    string? AutoMessageConfig,
    string? NotificationConfig,
    IReadOnlyList<OnboardingOpeningHourRequest>      OpeningHours,
    IReadOnlyList<OnboardingHolidayRequest>?         Holidays,
    IReadOnlyList<OnboardingEmployeeRequest>?        Employees,
    IReadOnlyList<OnboardingBlacklistRequest>?       Blacklist,
    IReadOnlyList<OnboardingLanguageRequest>?        LanguageRequests,
    IReadOnlyList<OnboardingIntegrationRequest>?     IntegrationRequests,
    string? PhoneNumber = null,
    bool PhoneIsOwned = false);

public record OnboardingOpeningHourRequest(
    short DayOfWeek,
    bool IsActive,
    string? StartTime,
    string? EndTime);

public record OnboardingHolidayRequest(
    string    Label,
    DateOnly? Start,
    DateOnly? End,
    bool      IsClosed = true);

public record OnboardingEmployeeRequest(
    string  Name,
    string? Role,
    string? Phone);

public record OnboardingBlacklistRequest(
    string  PhoneNumber,
    string? Reason);

public record OnboardingLanguageRequest(string Language, string? Email);
public record OnboardingIntegrationRequest(string Name, string? Category, string? Email, string? Note);

// ── Invoices (stub) ──────────────────────────────────────────────────────────

public record InvoiceDto(
    string Id,
    string Date,
    decimal Amount,
    string Status,
    string PdfUrl);
