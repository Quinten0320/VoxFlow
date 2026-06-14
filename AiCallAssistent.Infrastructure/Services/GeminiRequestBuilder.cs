using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;

namespace AiCallAssistent.Infrastructure.Services;

internal static class GeminiRequestBuilder
{
    public static byte[] BuildRequestBytes(
        string contentsJson,
        CallDispatchContext context,
        CompanyCallConfig? config,
        IGeminiFunctionDispatcher dispatcher,
        GeminiSettings settings)
    {
        var nowNl = NlTimeZone.Now;
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 8192);
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();

        writer.WritePropertyName("system_instruction");
        writer.WriteStartObject();
        writer.WritePropertyName("parts");
        writer.WriteStartArray();
        writer.WriteStartObject();

        var isWhatsApp = config?.IsWhatsApp == true;
        var medium = isWhatsApp ? "WhatsApp" : "de telefoon";

        var basePrompt = config?.SystemPrompt is { Length: > 0 } p ? p :
            $"""
            Je bent een vriendelijke AI-assistent die afspraken boekt voor bedrijf met ID {context.CompanyId}.
            Help de klant een afspraak te plannen via de beschikbare tools.
            Bevestig altijd de afspraakdetails voordat je daadwerkelijk een boeking maakt.
            Spreek altijd en uitsluitend Nederlands — gebruik nooit een andere taal.
            Communiceer natuurlijk en beknopt via {medium}.
            Alle tijden zijn in Nederlandse lokale tijd (Europe/Amsterdam).
            Vandaag is {nowNl:dddd, d MMMM yyyy} en de huidige tijd is {nowNl:HH:mm}.
            Los relatieve datums zoals "morgen", "volgende maandag" of "aanstaande dinsdag" op met de datum van vandaag.
            Vraag de klant nooit om een datum die je zelf kunt berekenen.

            REGELS VOOR TOOLS:
            - Zeg nooit "momentje" of "ik zoek het even op" voordat je een tool aanroept. Roep de tool meteen aan en geef daarna pas antwoord.
            - Vraagt de klant welke afspraken beschikbaar zijn? Roep ONMIDDELLIJK get_appointment_types aan. Reageer niet eerst met tekst.
            - Wil de klant boeken of vraagt hij naar beschikbaarheid? Roep ONMIDDELLIJK get_soonest_available of check_availability aan.
            - Haal altijd actuele data op via de tools voordat je antwoord geeft over diensten, beschikbaarheid of tijden.
            """;

        var channelRules = isWhatsApp
            ? """

              Je communiceert via WhatsApp. Je mag gebruik maken van tekst-opmaak zoals regeleinden en bullet points.
              """
            : """

              REGELS VOOR SPREEKSTIJL:
              - Gebruik NOOIT opsommingstekens, streepjes of genummerde lijsten. Dit is een telefoongesprek — geef opsommingen altijd als kommalijst, zoals: "technisch overleg van 30 minuten, een instapgesprek van 15 minuten, en een kennismaking van 20 minuten".
              """;

        var afterHoursInstructions = config?.AfterHoursMode switch
        {
            AiCallAssistent.Application.Constants.AfterHoursMode.CallbackOnly or
            AiCallAssistent.Application.Constants.AfterHoursMode.TryHuman =>
                """

                BUITEN OPENINGSTIJDEN — BELANGRIJK:
                Het bedrijf is op dit moment GESLOTEN. Boek GEEN afspraken.
                Jouw enige taak is het inplannen van een terugbelverzoek via schedule_callback.
                Roep altijd eerst get_opening_hours aan zodat je de beller kunt vertellen wanneer we beschikbaar zijn.
                Bevestig het terugbelvenster met de beller voordat je schedule_callback aanroept.
                """,
            AiCallAssistent.Application.Constants.AfterHoursMode.FullService =>
                """

                BUITEN OPENINGSTIJDEN:
                Het bedrijf is op dit moment gesloten, maar je kunt de beller volledig helpen.
                Je mag afspraken boeken, beschikbaarheid checken en terugbelverzoeken inplannen zoals normaal.
                Laat de beller weten dat het bedrijf momenteel gesloten is, maar dat je de afspraak gewoon kunt vastleggen.
                """,
            _ => null
        };

        writer.WriteString("text", basePrompt + afterHoursInstructions + channelRules);

        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("contents");
        writer.WriteRawValue(contentsJson);

        var toolsJson = new JsonArray { dispatcher.GetToolDeclarations(context.Branch, context.Features) }.ToJsonString();
        writer.WritePropertyName("tools");
        writer.WriteRawValue(toolsJson);

        writer.WritePropertyName("tool_config");
        writer.WriteStartObject();
        writer.WritePropertyName("function_calling_config");
        writer.WriteStartObject();
        writer.WriteString("mode", "AUTO");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    public static JsonObject UserTurn(string text) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
    };

    public static JsonObject ModelTextTurn(string text) => new()
    {
        ["role"] = "model",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
    };

    public static JsonObject ModelFunctionCallTurn(JsonObject functionCall) => new()
    {
        ["role"] = "model",
        // DeepClone to avoid the node being reparented away from the parsed response tree
        ["parts"] = new JsonArray { new JsonObject { ["functionCall"] = functionCall.DeepClone() } }
    };

    public static JsonObject FunctionResponseTurn(string name, JsonObject response) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray
        {
            new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = name,
                    ["response"] = response
                }
            }
        }
    };
}
