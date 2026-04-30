namespace AiCallAssistent.Application.Configuration;

public class SupabaseSettings
{
    public string ProjectUrl { get; set; } = string.Empty;

    public string Authority => $"{ProjectUrl}/auth/v1";
}
