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

        var dateTimeContext = $"""

            HUIDIGE DATUM EN TIJD: Vandaag is {nowNl:dddd, d MMMM yyyy}, de huidige tijd is {nowNl:HH:mm} (Europa/Amsterdam, CEST).
            Plan NOOIT een terugbelverzoek of afspraak op een tijdstip dat al verstreken is. Als een tijdvenster vandaag al voorbij is, plan dan voor een toekomstige dag.
            Los relatieve datums zoals "morgen", "volgende maandag" of "aanstaande dinsdag" op met de datum van vandaag als referentie.
            """;

        var basePrompt = config?.SystemPrompt is { Length: > 0 } p
            ? p + dateTimeContext
            : $"""
            Je bent een vriendelijke AI-assistent die afspraken boekt voor bedrijf met ID {context.CompanyId}.
            Help de klant een afspraak te plannen via de beschikbare tools.
            Bevestig altijd de afspraakdetails voordat je daadwerkelijk een boeking maakt.
            Spreek altijd en uitsluitend Nederlands — gebruik nooit een andere taal.
            Communiceer natuurlijk en beknopt via {medium}.
            Alle tijden zijn in Nederlandse lokale tijd (Europe/Amsterdam).
            {dateTimeContext.Trim()}
            Vraag de klant nooit om een datum die je zelf kunt berekenen.

            REGELS VOOR TOOLS:
            - Zeg nooit "momentje" of "ik zoek het even op" voordat je een tool aanroept. Roep de tool meteen aan en geef daarna pas antwoord.
            - Vraagt de klant welke afspraken beschikbaar zijn? Roep ONMIDDELLIJK get_appointment_types aan. Reageer niet eerst met tekst.
            - Noemt de beller een specifieke datum? Roep dan ONMIDDELLIJK check_availability aan voor die datum. Noemt hij ook een tijdsvoorkeur (bijv. "tussen 2 en 4 uur")? Geef dan ook from_time en until_time mee als "HH:MM".
            - Heeft de beller geen datum of tijdstip in gedachten? Roep dan get_soonest_available aan om het eerstvolgende vrije moment te vinden.
            - Als er meerdere tijdsloten beschikbaar zijn, bied de beller 2 of 3 opties aan in plaats van alleen de eerste.
            - Geeft create_appointment een foutmelding? Lees de fout en voer ONMIDDELLIJK de daarin genoemde tool aan — meestal check_availability voor dezelfde datum — om alternatieven te vinden en die aan te bieden.
            - Haal altijd actuele data op via de tools voordat je antwoord geeft over diensten, beschikbaarheid of tijden.
            """;

        var channelRules = isWhatsApp
            ? """

              Je communiceert via WhatsApp. Je mag gebruik maken van tekst-opmaak zoals regeleinden en bullet points.
              """
            : """

              REGELS VOOR SPREEKSTIJL:
              - Houd antwoorden KORT en BONDIG. Gebruik maximaal 1 à 2 zinnen per reactie. Geef direct antwoord zonder omhaal van woorden.
              - Gebruik NOOIT opsommingstekens, streepjes of genummerde lijsten. Dit is een telefoongesprek — geef opsommingen altijd als kommalijst, zoals: "technisch overleg van 30 minuten, een instapgesprek van 15 minuten, en een kennismaking van 20 minuten".
              - Noem tijden ALTIJD in gesproken taal. Gebruik het veld "spoken_time" uit de tool-respons — dat bevat de juiste gesproken tijd. Zeg dus "om kwart over 2 's middags", niet "14:15". Gebruik NOOIT digitale notaties zoals "09:00", "14:00" of "07:20".
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

        var behaviorSection = BuildBehaviorSection(config, nowNl);
        var sentimentSection = BuildSentimentEscalationSection(context.Features);
        writer.WriteString("text", basePrompt + behaviorSection + sentimentSection + afterHoursInstructions + channelRules);

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

    private static string BuildBehaviorSection(CompanyCallConfig? config, DateTimeOffset now)
    {
        if (config is null) return string.Empty;
        var sb = new System.Text.StringBuilder();

        if (config.AssistantName is { Length: > 0 } name)
            sb.AppendLine($"\nJe naam is {name}.");

        if (config.Tone is { Length: > 0 } tone)
        {
            var toneDesc = tone.ToLowerInvariant() switch
            {
                "vriendelijk" =>
                    """
                    TOON — Vriendelijk:
                    Je bent warm, toegankelijk en informeel. Spreek de beller altijd aan met "je" en "jij", nooit met "u".
                    Je voelt als een behulpzame collega — oprecht betrokken maar niet overdreven.
                    Gebruik korte, actieve zinnen. Vermijd formele of stijve formuleringen.
                    Voorbeeld afspraak: "Super, ik heb je afspraak ingepland voor donderdag om twee uur. Nog ergens anders mee helpen?"
                    Voorbeeld doorverbinden: "Oké, ik schakel je even door naar een collega. Momentje!"
                    """,

                "professioneel" =>
                    """
                    TOON — Professioneel:
                    Je bent formeel, beleefd en zelfverzekerd. Spreek de beller altijd aan met "u", nooit met "je" of "jij".
                    Je toon is rustig en beheerst — als een ervaren receptioniste van een gevestigd kantoor.
                    Gebruik volledige, verzorgde zinnen. Geen informele uitdrukkingen of tussenwoordjes.
                    Voorbeeld afspraak: "Uw afspraak is bevestigd voor donderdag 14 maart om 14.00 uur. Heeft u verder nog vragen?"
                    Voorbeeld doorverbinden: "Ik verbind u door met een medewerker. Een moment geduld alstublieft."
                    """,

                "neutraal" =>
                    """
                    TOON — Neutraal:
                    Je bent helder en efficiënt. Spreek de beller aan met "u", maar zonder uitgebreide beleefdheidsformules.
                    Ga direct to the point. Vermijd zowel overdreven warmte als overdreven formaliteit.
                    Houd zinnen kort en informatief.
                    Voorbeeld afspraak: "Afspraak staat ingepland op donderdag 14 maart om 14.00 uur. Nog iets anders?"
                    Voorbeeld doorverbinden: "Ik schakel u door. Moment."
                    """,

                "empathisch" =>
                    """
                    TOON — Empathisch:
                    Je luistert actief en erkent de situatie van de beller. Spreek de beller aan met "u", maar je toon is zacht en menselijk.
                    Geef de beller het gevoel dat hij/zij gehoord wordt voor je verder gaat. Pas je tempo aan — rustig en begripvol.
                    Geschikt voor gevoelige situaties. Geen haast, geen afstandelijkheid.
                    Voorbeeld afspraak: "Geen probleem, ik plan uw afspraak graag opnieuw in. Wanneer schikt het u het beste?"
                    Voorbeeld doorverbinden: "Ik begrijp het. Ik zorg dat u direct met de juiste persoon wordt verbonden."
                    """,

                _ => null
            };
            if (toneDesc != null) sb.AppendLine($"\n{toneDesc}");
        }

        if (config.AutoTimeGreeting)
        {
            var greeting = now.Hour switch { < 12 => "Goedemorgen", < 18 => "Goedemiddag", _ => "Goedeavond" };
            sb.AppendLine($"\nBegin je EERSTE zin altijd met \"{greeting}\".");
        }

        if (config.UseCallerName)
            sb.AppendLine("\nAls je de naam van de beller al weet, spreek hem/haar dan bij naam aan in je begroeting.");

        if (config.BehaviorInstructions is { Length: > 0 } bi)
            sb.AppendLine($"\nAANVULLENDE GEDRAGSINSTRUCTIES:\n{bi}");

        if (config.TopicsYes is { Length: > 0 } yes)
            sb.AppendLine($"\nONDERWERPEN DIE JE BEHANDELT: Je helpt bellers uitsluitend met: {string.Join(", ", yes)}. Bij elk ander onderwerp zeg je vriendelijk dat je hier niet bij kunt helpen.");

        if (config.TopicsNo is { Length: > 0 } no)
        {
            var fallbackInstruction = config.FallbackBehavior switch
            {
                "terugbellen"   => "plan daarna een terugbelverzoek in via de schedule_callback tool",
                "doorverbinden" => "verbind daarna door naar een medewerker via de transfer_to_human tool",
                _               => "leg vriendelijk uit dat je hierover geen informatie hebt"
            };
            sb.AppendLine($"\nONDERWERPEN DIE JE NIET BEHANDELT: {string.Join(", ", no)}.");
            sb.AppendLine($"Als een beller vraagt over een van deze onderwerpen: zeg dat je hier niet mee kunt helpen, en {fallbackInstruction}.");
        }

        if (config.RoutingRulesJson is { Length: > 0 } rulesJson)
        {
            var routingText = ParseRoutingRules(rulesJson);
            if (routingText is { Length: > 0 })
                sb.AppendLine($"\nDOORSCHAKELREGELS PER ONDERWERP:\n{routingText}");
        }

        return sb.ToString();
    }

    private static string BuildSentimentEscalationSection(AiCallAssistent.Application.DTOs.CompanyFeatures? features)
    {
        if (features?.TransferToHuman != true) return string.Empty;
        return """

            SENTIMENTDETECTIE:
            Let op signalen van frustratie of agressie bij de beller: herhaalde klachten, verheven stem, uitspraken zoals "dit is belachelijk", "ik wil nu een medewerker", "jullie helpen me niet".
            Als je twee of meer van deze signalen achter elkaar waarneemt, bied dan proactief aan om de beller door te verbinden: "Ik merk dat dit frustrerend is. Wilt u dat ik u doorverbind met een medewerker?"
            Als de beller dit bevestigt, roep dan onmiddellijk transfer_to_human aan.
            """;
    }

    private static string? ParseRoutingRules(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sb = new System.Text.StringBuilder();

            if (root.TryGetProperty("branchTopics", out var bt))
                foreach (var kv in bt.EnumerateObject())
                    sb.AppendLine($"- {kv.Name} → {MapAction(kv.Value.GetString())}");

            System.Text.Json.JsonElement? arr = null;
            if (root.TryGetProperty("topicActions", out var ta)) arr = ta;
            else if (root.TryGetProperty("customTopics", out var ct)) arr = ct;

            if (arr.HasValue)
                foreach (var item in arr.Value.EnumerateArray())
                {
                    var topic  = item.TryGetProperty("topic",  out var t) ? t.GetString()
                               : item.TryGetProperty("name",   out var n) ? n.GetString() : null;
                    var action = item.TryGetProperty("action", out var a) ? a.GetString() : null;
                    if (topic != null && action != null)
                        sb.AppendLine($"- {topic} → {MapAction(action)}");
                }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch { return null; }
    }

    private static string MapAction(string? a) => a switch
    {
        "Doorschakelen"   => "doorschakelen naar medewerker",
        "Terugbelverzoek" => "terugbelverzoek inplannen",
        "doorschakelen"   => "doorschakelen naar medewerker",
        "terugbel"        => "terugbelverzoek inplannen",
        _                 => "assistent handelt zelf af"
    };

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
