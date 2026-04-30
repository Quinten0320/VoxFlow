using System.Net.Http.Headers;
using System.Text;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Services shared by both the call API and the web dashboard API.
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Supabase Transaction Pooler needs these two options:
        // No Reset On Close: prevents DISCARD ALL (not supported by Supavisor)
        // Max Auto Prepare=0: disables prepared statements (not supported in transaction mode)
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            + ";No Reset On Close=true;Max Auto Prepare=0";
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString), ServiceLifetime.Scoped);

        services.AddScoped<IAppointmentService, AppointmentService>();

        services.Configure<OutlookSettings>(configuration.GetSection("Outlook"));
        services.AddHttpClient<IOutlookCalendarService, OutlookCalendarService>();

        // Named Twilio client carries Basic auth and is used for both recording downloads and WhatsApp.
        services.Configure<TwilioSettings>(configuration.GetSection("Twilio"));
        services.AddHttpClient("Twilio", (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddScoped<IWhatsAppService, WhatsAppService>();

        return services;
    }

    /// <summary>
    /// Services only used by the call pipeline API.
    /// </summary>
    public static IServiceCollection AddCallPipelineInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GeminiSettings>(configuration.GetSection("Gemini"));
        services.AddHttpClient<IGeminiService, GeminiService>();
        services.AddScoped<IGeminiFunctionDispatcher, GeminiFunctionDispatcher>();

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();

        services.Configure<ElevenLabsSettings>(configuration.GetSection("ElevenLabs"));
        services.AddHttpClient<IElevenLabsService, ElevenLabsService>();

        services.Configure<DeepgramSettings>(configuration.GetSection("Deepgram"));
        services.AddHttpClient<IDeepgramService, DeepgramService>();

        services.Configure<AssistantSettings>(configuration.GetSection("Assistant"));

        services.AddSingleton<IAudioStore, InMemoryAudioStore>();

        return services;
    }
}
