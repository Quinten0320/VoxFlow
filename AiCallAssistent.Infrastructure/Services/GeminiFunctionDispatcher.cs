using System.Text.Json.Nodes;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;

namespace AiCallAssistent.Infrastructure.Services;

public class GeminiFunctionDispatcher(
    IAppointmentService appointments,
    IWhatsAppService whatsApp) : IGeminiFunctionDispatcher
{
    public async Task<object> DispatchAsync(CallDispatchContext context, string functionName, JsonNode? args)
    {
        string GetString(string key) => args![key]!.GetValue<string>();

        return functionName switch
        {
            "get_appointment_types" =>
                await appointments.GetAppointmentTypesAsync(context.CompanyId),

            "get_soonest_available" =>
                await appointments.GetSoonestAvailableAsync(context.CompanyId, GetString("type")),

            "check_availability" =>
                await appointments.GetAvailabilityAsync(
                    context.CompanyId,
                    GetString("type"),
                    DateOnly.Parse(GetString("date"))),

            "create_appointment" =>
                await CreateAppointmentAsync(context, args),

            "cancel_appointment" =>
                await CancelAppointmentAsync(context, args),

            "transfer_to_human" =>
                TransferToHuman(context),

            _ => throw new ArgumentException($"Unknown function '{functionName}'")
        };
    }

    private async Task<object> CreateAppointmentAsync(CallDispatchContext context, JsonNode? args)
    {
        var request = new CreateAppointmentRequest
        {
            CompanyId    = context.CompanyId,
            Type         = args!["type"]!.GetValue<string>(),
            StartTime    = ParseNlAware(args["start_time"]!.GetValue<string>()),
            EmployeeId   = args["employee_id"] is JsonNode eId ? eId.GetValue<long>() : null,
            CustomerName = args["customer_name"]?.GetValue<string>(),
            Description  = args["description"]?.GetValue<string>() ?? ""
        };

        var result = await appointments.CreateAppointmentAsync(request);

        if (!string.IsNullOrWhiteSpace(context.CallerNumber))
        {
            await whatsApp.SendAppointmentConfirmationAsync(
                context.CallerNumber,
                result.Type,
                result.StartTime,
                result.EmployeeName);
        }

        return result;
    }

    private async Task<object> CancelAppointmentAsync(CallDispatchContext context, JsonNode? args)
    {
        if (args?["appointment_id"] is not JsonNode idNode)
            return new { success = false, error = "appointment_id is required" };

        var cancelled = await appointments.CancelAppointmentAsync(idNode.GetValue<long>(), context.CompanyId);

        return cancelled
            ? new { success = true, message = "Appointment cancelled successfully." }
            : new { success = false, error = "Appointment not found or does not belong to this company." };
    }

    private static object TransferToHuman(CallDispatchContext context)
    {
        // Returning success signals Gemini to say a farewell; the controller then
        // replaces <Record> with <Dial> to EscalationNumber.
        return string.IsNullOrWhiteSpace(context.EscalationNumber)
            ? new { success = false, error = "No escalation number is configured for this company." }
            : new { success = true, message = "Transferring call to a human representative." };
    }

    public JsonObject GetToolDeclarations() => new()
    {
        ["function_declarations"] = new JsonArray
        {
            FunctionDeclaration("get_appointment_types",
                "Get all available appointment types for the company with their duration in minutes.",
                new JsonObject(), []),

            FunctionDeclaration("get_soonest_available",
                "Find the soonest available appointment slot for a given type.",
                new JsonObject
                {
                    ["type"] = Param("string", "Appointment type name, e.g. hair_cutting_male")
                },
                ["type"]),

            FunctionDeclaration("check_availability",
                "Get all available time slots for a specific date and appointment type.",
                new JsonObject
                {
                    ["type"] = Param("string"),
                    ["date"] = Param("string", "Date in YYYY-MM-DD format")
                },
                ["type", "date"]),

            FunctionDeclaration("create_appointment",
                "Book an appointment. Always confirm the details with the caller before calling this.",
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

            FunctionDeclaration("transfer_to_human",
                "Transfer the caller to a human representative. " +
                "ONLY call this when the caller EXPLICITLY asks to speak to a person, employee, or agent. " +
                "Never call this proactively or just because a question is difficult.",
                new JsonObject(), [])
        }
    };

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

    private static JsonObject FunctionDeclaration(string name, string description,
        JsonObject properties, string[] required) => new()
    {
        ["name"]        = name,
        ["description"] = description,
        ["parameters"]  = new JsonObject
        {
            ["type"]       = "object",
            ["properties"] = properties,
            ["required"]   = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)).ToArray())
        }
    };

    private static JsonObject Param(string type, string? description = null)
    {
        var obj = new JsonObject { ["type"] = type };
        if (description is not null) obj["description"] = description;
        return obj;
    }
}
