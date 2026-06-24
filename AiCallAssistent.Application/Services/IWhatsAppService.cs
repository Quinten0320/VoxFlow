namespace AiCallAssistent.Application.Services;

public interface IWhatsAppService
{
    /// <summary>
    /// Company-aware send: checks WhatsAppActive flag, enforces MaxWhatsAppPerMonth quota,
    /// resolves from-number from AssistantSettings, and logs to whatsapp_message_log.
    /// No-ops silently if WhatsApp is not active or quota is exceeded.
    /// This is the only method callers outside WhatsAppService should use.
    /// </summary>
    Task SendForCompanyAsync(short companyId, string toNumber, string message, string messageType);
}
