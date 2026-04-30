namespace AiCallAssistent.Application.Configuration;

/// <summary>
/// Per-assistant configuration. Currently served from appsettings.json.
///
/// SWITCHING TO DB: Once the assistant_settings table is integrated, replace
/// IOptions&lt;AssistantSettings&gt; injection with a DB lookup service that reads
/// assistant_settings.greetings_message and assistant_settings.prompt per company.
/// The interface of the consuming services does not need to change.
/// </summary>
public class AssistantSettings
{
    /// <summary>
    /// Welcome message spoken when a call is answered.
    /// Use {company} as a placeholder — it is replaced with the company name from the DB.
    ///
    /// Future: read per-company from assistant_settings.greetings_message.
    /// </summary>
    public string WelcomeMessage { get; set; } =
        "Bedankt voor uw oproep. U spreekt met de AI-assistent van {company}. Hoe kan ik u vandaag helpen?";

    /// <summary>
    /// Fallback company ID used until phone number → company lookup is implemented.
    ///
    /// Future: derive from the called number via the phone_numbers table
    /// (look up phone_numbers.ai_phone_number matching the Twilio To field).
    /// </summary>
    public short DefaultCompanyId { get; set; } = 1;
}
