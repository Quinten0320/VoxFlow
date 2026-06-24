using System.Text.Json.Nodes;
using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;

namespace AiCallAssistent.Infrastructure.Services;

public class GeminiFunctionDispatcher : IGeminiFunctionDispatcher
{
    private readonly Dictionary<string, Func<CallDispatchContext, JsonNode?, Task<object>>> _handlers;

    // Branch name → action that appends that branch's extra tool declarations.
    // Add new branches here without touching any other code.
    private static readonly Dictionary<string, Action<JsonArray>> BranchToolAdders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [CompanyBranch.Makelaar] = declarations => declarations.Add(FunctionDeclaration(
                "get_property_info",
                "Look up property listing information by address or listing ID. " +
                "Call this when the caller asks about a specific property.",
                new JsonObject { ["query"] = Param("string", "Address or listing ID to search for") },
                ["query"]))
        };

    public GeminiFunctionDispatcher(
        IAppointmentService appointments,
        IDepartmentService departments,
        IOpeningHoursService openingHours,
        IWhatsAppService whatsApp,
        ICallbackService callbacks,
        IPropertyInfoProvider propertyInfo)
    {
        _handlers = new Dictionary<string, Func<CallDispatchContext, JsonNode?, Task<object>>>
        {
            ["get_appointment_types"] = async (ctx, _) =>
                await appointments.GetAppointmentTypesAsync(ctx.CompanyId),

            ["get_soonest_available"] = async (ctx, args) =>
                await appointments.GetSoonestAvailableAsync(ctx.CompanyId, Arg(args, "type")),

            ["check_availability"] = async (ctx, args) =>
                await appointments.GetAvailabilityAsync(
                    ctx.CompanyId, Arg(args, "type"), DateOnly.Parse(Arg(args, "date")),
                    args?["from_time"]?.GetValue<string>(),
                    args?["until_time"]?.GetValue<string>()),

            ["create_appointment"] = async (ctx, args) =>
                await CreateAppointmentAsync(ctx, args, appointments, whatsApp),

            ["cancel_appointment"] = async (ctx, args) =>
                await CancelAppointmentAsync(ctx, args, appointments),

            ["reschedule_appointment"] = async (ctx, args) =>
                await RescheduleAppointmentAsync(ctx, args, appointments),

            ["get_departments"] = async (ctx, _) =>
                await departments.GetDepartmentsAsync(ctx.CompanyId),

            ["transfer_to_department"] = (ctx, args) =>
                Task.FromResult(TransferToDepartment(ctx, Arg(args, "department_name"))),

            ["transfer_to_human"] = (ctx, _) =>
                Task.FromResult(TransferToHuman(ctx)),

            ["get_opening_hours"] = async (ctx, args) =>
                await GetOpeningHoursAsync(ctx.CompanyId, args, openingHours),

            ["schedule_callback"] = async (ctx, args) =>
                await ScheduleCallbackAsync(ctx, args, callbacks, whatsApp),

            ["get_property_info"] = async (_, args) =>
                await GetPropertyInfoAsync(args, propertyInfo),
        };
    }

    public async Task<object> DispatchAsync(
        CallDispatchContext context,
        string functionName,
        JsonNode? args,
        IConversationStore? store = null,
        string? conversationId = null)
    {
        if (!_handlers.TryGetValue(functionName, out var handler))
            throw new ArgumentException($"Unknown function '{functionName}'");

        var result = await handler(context, args);

        if (store != null && conversationId != null)
        {
            var outcome = functionName switch
            {
                "create_appointment"   => "Afspraak ingepland",
                "schedule_callback"    => "Terugbelverzoek",
                "transfer_to_department" or "transfer_to_human" => "Doorgeschakeld",
                _ => null
            };
            if (outcome != null)
                store.SetCallOutcome(conversationId, outcome);
        }

        return result;
    }

    // ── Static dispatch helpers ──────────────────────────────────────────────

    private static async Task<object> CreateAppointmentAsync(
        CallDispatchContext context, JsonNode? args,
        IAppointmentService appointments, IWhatsAppService whatsApp)
    {
        var request = new CreateAppointmentRequest
        {
            CompanyId         = context.CompanyId,
            Type              = args!["type"]!.GetValue<string>(),
            StartTime         = ParseNlAware(args["start_time"]!.GetValue<string>()),
            EmployeeId        = args["employee_id"] is JsonNode eId ? eId.GetValue<long>() : null,
            CustomerName      = args["customer_name"]?.GetValue<string>(),
            Description       = args["description"]?.GetValue<string>() ?? "",
            CallerPhoneNumber = context.CallerNumber
        };

        var result = await appointments.CreateAppointmentAsync(request);

        var features = context.Features ?? CompanyFeatures.Default;
        if (features.WhatsAppConfirmation && !string.IsNullOrWhiteSpace(context.CallerNumber))
        {
            var nl = NlTimeZone.ConvertFromUtc(result.StartTime);
            var msg =
                $"✅ Uw afspraak is bevestigd!\n\n" +
                $"📅 {nl:dddd d MMMM} om {nl:HH:mm}\n" +
                $"💇 {result.Type}\n" +
                $"👤 {result.EmployeeName}\n\n" +
                $"Wilt u de afspraak wijzigen? Bel ons dan.";
            await whatsApp.SendForCompanyAsync(context.CompanyId, context.CallerNumber, msg, "confirmation");
        }

        return result;
    }


    private static async Task<object> CancelAppointmentAsync(
        CallDispatchContext context, JsonNode? args, IAppointmentService appointments)
    {
        if (args?["appointment_id"] is not JsonNode idNode)
            return new { success = false, error = "appointment_id is required" };

        var cancelled = await appointments.CancelAppointmentAsync(idNode.GetValue<long>(), context.CompanyId);

        return cancelled
            ? new { success = true, message = "Appointment cancelled successfully." }
            : new { success = false, error = "Appointment not found or does not belong to this company." };
    }

    private static async Task<object> RescheduleAppointmentAsync(
        CallDispatchContext context, JsonNode? args, IAppointmentService appointments)
    {
        if (args?["appointment_id"] is not JsonNode idNode)
            return new { success = false, error = "appointment_id is required" };
        if (args["new_start_time"] is not JsonNode startNode)
            return new { success = false, error = "new_start_time is required" };

        try
        {
            var result = await appointments.RescheduleAppointmentAsync(
                idNode.GetValue<long>(),
                ParseNlAware(startNode.GetValue<string>()),
                context.CompanyId);

            return new
            {
                success = true,
                result.AppointmentId,
                result.EmployeeName,
                result.Type,
                StartTime = result.StartTime.ToString("o"),
                EndTime   = result.EndTime.ToString("o"),
                message = "Appointment rescheduled successfully."
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private static object TransferToDepartment(CallDispatchContext context, string departmentName)
    {
        if (context.DepartmentPhones?.ContainsKey(departmentName) == true)
            return new { success = true, message = $"Transferring call to the {departmentName} department." };

        return new { success = false, error = $"Department '{departmentName}' is not available." };
    }

    private static object TransferToHuman(CallDispatchContext context)
    {
        return string.IsNullOrWhiteSpace(context.EscalationNumber)
            ? new { success = false, error = "No escalation number is configured for this company." }
            : new { success = true, message = "Transferring call to a human representative." };
    }

    private static async Task<object> GetOpeningHoursAsync(
        short companyId, JsonNode? args, IOpeningHoursService openingHours)
    {
        var dateStr = args?["date"]?.GetValue<string>();
        var date = dateStr is not null ? DateOnly.Parse(dateStr) : DateOnly.FromDateTime(NlTimeZone.Now.DateTime);

        var ranges = await openingHours.GetOpeningRangesForDateAsync(companyId, date);
        if (ranges.Count == 0)
            return new { date = date.ToString("yyyy-MM-dd"), is_open = false, ranges = (object[])[] };

        return new
        {
            date = date.ToString("yyyy-MM-dd"),
            is_open = true,
            ranges = ranges.Select(r => new { from = r.Start.ToString("HH:mm"), until = r.End.ToString("HH:mm") }).ToArray()
        };
    }

    private static async Task<object> ScheduleCallbackAsync(
        CallDispatchContext context, JsonNode? args,
        ICallbackService callbacks, IWhatsAppService whatsApp)
    {
        var callerName     = args!["caller_name"]!.GetValue<string>();
        var reason         = args["reason"]!.GetValue<string>();
        var scheduledFrom  = ParseNlAware(args["scheduled_from"]!.GetValue<string>());
        var scheduledUntil = ParseNlAware(args["scheduled_until"]!.GetValue<string>());

        var id = await callbacks.ScheduleCallbackAsync(
            context.CompanyId,
            context.CallerNumber ?? string.Empty,
            callerName, reason, scheduledFrom, scheduledUntil);

        var features = context.Features ?? CompanyFeatures.Default;
        if (features.WhatsAppConfirmation && !string.IsNullOrWhiteSpace(context.CallerNumber))
        {
            var fromNl  = NlTimeZone.ConvertFromUtc(scheduledFrom);
            var untilNl = NlTimeZone.ConvertFromUtc(scheduledUntil);
            var msg =
                $"📞 Terugbelverzoek ontvangen, {callerName}!\n\n" +
                $"Wij bellen u terug op {fromNl:dddd d MMMM} tussen {fromNl:HH:mm} en {untilNl:HH:mm}.\n\n" +
                $"Staat u ergens anders voor open? Bel ons dan even.";
            await whatsApp.SendForCompanyAsync(context.CompanyId, context.CallerNumber, msg, "callback");
        }

        return new { success = true, callback_request_id = id, message = "Callback request scheduled." };
    }

    private static async Task<object> GetPropertyInfoAsync(JsonNode? args, IPropertyInfoProvider propertyInfo)
    {
        var query = args!["query"]!.GetValue<string>();
        var result = await propertyInfo.GetPropertyInfoAsync(query);
        if (result is null)
            return new { found = false, message = "No listing found for the given query." };

        return new
        {
            found = true,
            result.Address,
            result.Price,
            result.Description,
            result.Status,
            result.Url
        };
    }

    // ── Tool declarations ────────────────────────────────────────────────────

    public JsonObject GetToolDeclarations(string? branch = null, CompanyFeatures? features = null)
    {
        var f = features ?? CompanyFeatures.Default;
        var declarations = new JsonArray
        {
            FunctionDeclaration("get_appointment_types",
                "Get all available appointment types for the company with their duration in minutes.",
                new JsonObject(), []),

            FunctionDeclaration("get_soonest_available",
                "Find the single earliest available appointment slot across all upcoming days. " +
                "Use this ONLY when the caller has not mentioned a preferred date or time. " +
                "If the caller mentions a date or time window, use check_availability instead.",
                new JsonObject
                {
                    ["type"] = Param("string", "Appointment type name, e.g. hair_cutting_male")
                },
                ["type"]),

            FunctionDeclaration("check_availability",
                "Get available time slots for a specific date and appointment type. " +
                "Always use this when the caller mentions a specific date. " +
                "When the caller also states a preferred time window (e.g. 'between 14:00 and 16:00'), pass from_time and until_time to narrow results. " +
                "Returns at most 5 slots so you can present options to the caller.",
                new JsonObject
                {
                    ["type"]       = Param("string"),
                    ["date"]       = Param("string", "Date in YYYY-MM-DD format"),
                    ["from_time"]  = Param("string", "Optional: earliest slot start time in HH:MM, e.g. \"14:00\""),
                    ["until_time"] = Param("string", "Optional: latest slot start time (exclusive) in HH:MM, e.g. \"16:00\"")
                },
                ["type", "date"]),

            FunctionDeclaration("create_appointment",
                "Book an appointment. Always confirm the details with the caller before calling this. " +
                "If this returns an error, read the error message — it tells you exactly what to do next (usually: call check_availability for the same date to find available slots).",
                new JsonObject
                {
                    ["type"]          = Param("string"),
                    ["start_time"]    = Param("string", "ISO 8601 datetime, e.g. 2026-04-07T09:00:00+02:00"),
                    ["employee_id"]   = Param("integer", "Optional: specific employee to book with"),
                    ["customer_name"] = Param("string", "Name of the caller, as given during the conversation"),
                    ["description"]   = Param("string", "Optional extra notes about the appointment")
                },
                ["type", "start_time"]),

            FunctionDeclaration("cancel_appointment",
                "Cancel an existing appointment by its ID. Always ask the caller to confirm before cancelling.",
                new JsonObject
                {
                    ["appointment_id"] = Param("integer", "The ID of the appointment to cancel")
                },
                ["appointment_id"]),

            FunctionDeclaration("reschedule_appointment",
                "Move an existing appointment to a new date/time. " +
                "Always call check_availability first to find a free slot, then confirm the new time with the caller before calling this.",
                new JsonObject
                {
                    ["appointment_id"]  = Param("integer", "The ID of the appointment to reschedule"),
                    ["new_start_time"]  = Param("string",  "New start time — ISO 8601 datetime, e.g. 2026-04-07T14:00:00+02:00")
                },
                ["appointment_id", "new_start_time"])
        };

        if (f.DepartmentRouting)
        {
            declarations.Add(FunctionDeclaration("get_departments",
                "Get all departments the caller can be transferred to.",
                new JsonObject(), []));

            declarations.Add(FunctionDeclaration("transfer_to_department",
                "Transfer the caller to a specific department. " +
                "Call get_departments first if you don't yet know which departments are available. " +
                "ONLY call this when the caller explicitly asks for a specific department.",
                new JsonObject
                {
                    ["department_name"] = Param("string", "The name of the department, as returned by get_departments")
                },
                ["department_name"]));
        }

        if (f.TransferToHuman)
        {
            declarations.Add(FunctionDeclaration("transfer_to_human",
                "Transfer the caller to a human representative when no specific department is requested. " +
                "ONLY call this when the caller EXPLICITLY asks to speak to a person, employee, or agent. " +
                "Never call this proactively or just because a question is difficult.",
                new JsonObject(), []));
        }

        if (f.CallbackRequests)
        {
            declarations.Add(FunctionDeclaration("get_opening_hours",
                "Get the opening hours for a specific date. " +
                "Call this before scheduling a callback so you can tell the caller when we are available.",
                new JsonObject
                {
                    ["date"] = Param("string", "Date in YYYY-MM-DD format. Defaults to today if omitted.")
                },
                []));

            declarations.Add(FunctionDeclaration("schedule_callback",
                "Schedule a callback request. " +
                "Call get_opening_hours first to check availability, then confirm the window with the caller before scheduling.",
                new JsonObject
                {
                    ["caller_name"]     = Param("string", "Full name of the caller"),
                    ["reason"]          = Param("string", "Reason the caller wants to be called back"),
                    ["scheduled_from"]  = Param("string", "Start of the callback window — ISO 8601 datetime"),
                    ["scheduled_until"] = Param("string", "End of the callback window — ISO 8601 datetime")
                },
                ["caller_name", "reason", "scheduled_from", "scheduled_until"]));
        }

        if (f.BranchTools && branch is not null
            && BranchToolAdders.TryGetValue(branch, out var addBranchTools))
        {
            addBranchTools(declarations);
        }

        return new JsonObject { ["function_declarations"] = declarations };
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses a datetime string from Gemini. Strings without an offset (e.g. "2026-04-07T09:00:00")
    /// are treated as Amsterdam local time, not UTC.
    /// </summary>
    private static DateTimeOffset ParseNlAware(string value)
    {
        var hasOffset = value.EndsWith('Z') ||
                        (value.Length > 19 && (value[19] == '+' || value[19] == '-'));

        if (hasOffset)
            return DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

        var dt = DateTime.Parse(value);
        return new DateTimeOffset(dt, NlTimeZone.Info.GetUtcOffset(dt));
    }

    private static string Arg(JsonNode? args, string key) => args![key]!.GetValue<string>();

    private static JsonObject FunctionDeclaration(string name, string description,
        JsonObject properties, string[] required) => new()
    {
        ["name"]        = name,
        ["description"] = description,
        ["parameters"]  = new JsonObject
        {
            ["type"]       = "object",
            ["properties"] = properties,
            ["required"]   = new JsonArray([..required.Select(r => (JsonNode)JsonValue.Create(r))])
        }
    };

    private static JsonObject Param(string type, string? description = null)
    {
        var obj = new JsonObject { ["type"] = type };
        if (description is not null) obj["description"] = description;
        return obj;
    }
}
