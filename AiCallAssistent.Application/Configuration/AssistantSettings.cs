namespace AiCallAssistent.Application.Configuration;

public class AssistantSettings
{
    /// <summary>{company} is replaced with the company name at runtime.</summary>
    public string WelcomeMessage { get; set; } =
        "Bedankt voor uw oproep. U spreekt met de AI-assistent van {company}. Hoe kan ik u vandaag helpen?";

    /// <summary>Fallback when no phone number row matches the incoming call.</summary>
    public short DefaultCompanyId { get; set; } = 1;
}
