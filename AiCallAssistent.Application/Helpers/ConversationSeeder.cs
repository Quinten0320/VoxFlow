using System.Text.Json.Nodes;

namespace AiCallAssistent.Application.Helpers;

/// <summary>
/// Builds the initial conversation turns seeded into every new call session.
/// Seeds a fake get_appointment_types call/response so Gemini has type info from the start,
/// then appends the welcome message as the model's first turn.
/// </summary>
public static class ConversationSeeder
{
    public static JsonArray BuildInitialTurns(string welcomeText, JsonNode? appointmentTypesNode)
    {
        var turns = new JsonArray();

        if (appointmentTypesNode is not null)
        {
            // Gemini requires a user turn before a functionCall turn.
            turns.Add(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "Welke afspraken bieden jullie aan?" } }
            });
            turns.Add(new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = "get_appointment_types",
                            ["args"] = new JsonObject()
                        }
                    }
                }
            });
            turns.Add(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = "get_appointment_types",
                            ["response"] = new JsonObject { ["result"] = appointmentTypesNode }
                        }
                    }
                }
            });
        }

        turns.Add(new JsonObject
        {
            ["role"] = "model",
            ["parts"] = new JsonArray { new JsonObject { ["text"] = welcomeText } }
        });

        return turns;
    }
}
