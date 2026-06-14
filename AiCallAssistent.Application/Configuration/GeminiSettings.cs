namespace AiCallAssistent.Application.Configuration;

public class GeminiSettings
{
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>Google Cloud project ID that has Vertex AI enabled.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Vertex AI region. Use europe-west4 (Netherlands) for GDPR/AVG compliance.</summary>
    public string Location { get; set; } = "europe-west4";

    /// <summary>
    /// Optional path to a service account JSON key file.
    /// Leave empty to use Application Default Credentials.
    /// In development, run: gcloud auth application-default login
    /// </summary>
    public string ServiceAccountKeyPath { get; set; } = string.Empty;
}
