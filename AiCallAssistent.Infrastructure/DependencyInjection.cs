using System.Net.Http.Headers;
using System.Text;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services;
using AiCallAssistent.Infrastructure.Services.Email;
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

        // AddDbContextFactory also registers AppDbContext as scoped — no separate AddDbContext needed.
        services.AddDbContextFactory<AppDbContext>(
            options => options.UseNpgsql(connectionString),
            ServiceLifetime.Scoped);

        services.AddScoped<ICompanyPackageService, CompanyPackageService>();
        services.AddScoped<IOpeningHoursService, OpeningHoursService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<ICallbackService, CallbackService>();
        services.AddScoped<IPropertyInfoProvider, StubPropertyInfoProvider>();
        services.AddScoped<IBotScheduleService, BotScheduleService>();

        services.Configure<OutlookSettings>(configuration.GetSection("Outlook"));
        services.AddHttpClient<IOutlookCalendarService, OutlookCalendarService>();

        // Named Twilio client carries Basic auth (AccountSid:AuthToken).
        // AccountSid:AuthToken is accepted by all Twilio REST APIs including Verify;
        // API Key auth is NOT accepted by the Verify API.
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
    /// Gmail SMTP email services — shared between all API processes that send transactional email.
    /// </summary>
    public static IServiceCollection AddEmailInfrastructure(this IServiceCollection services,
                                                            IConfiguration configuration)
    {
        services.Configure<GmailSettings>(configuration.GetSection("Gmail"));
        services.AddSingleton<IEmailService, SmtpEmailService>();
        services.AddSingleton<EmailTemplateService>();
        services.AddSingleton<EmailSender>();
        services.AddHostedService<EmailJobBackgroundService>();
        return services;
    }

    /// <summary>
    /// Background services that run in the call pipeline API process.
    /// </summary>
    public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
    {
        services.AddHostedService<AppointmentNotificationService>();
        services.AddHostedService<DataRetentionService>();
        return services;
    }

    /// <summary>
    /// Services only used by the call pipeline API.
    /// </summary>
    public static IServiceCollection AddCallPipelineInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GeminiSettings>(configuration.GetSection("Gemini"));
        services.AddSingleton<IVertexAiTokenProvider, VertexAiTokenProvider>();
        services.AddHttpClient<IGeminiService, GeminiService>();
        services.AddScoped<IGeminiFunctionDispatcher, GeminiFunctionDispatcher>();
        services.AddScoped<ICallSetupService, CallSetupService>();

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();

        services.Configure<ElevenLabsSettings>(configuration.GetSection("ElevenLabs"));
        services.AddHttpClient<IElevenLabsService, ElevenLabsService>();

        services.Configure<DeepgramSettings>(configuration.GetSection("Deepgram"));
        services.AddHttpClient<IDeepgramService, DeepgramService>();

        services.Configure<AssistantSettings>(configuration.GetSection("Assistant"));

        services.AddSingleton<IAudioStore, InMemoryAudioStore>();

        var sttProvider = configuration.GetSection("Deepgram")["SttProvider"] ?? "flux";
        if (sttProvider.Equals("nova3", StringComparison.OrdinalIgnoreCase))
            services.AddTransient<ISttStreamingService, Nova3StreamingService>();
        else
            services.AddTransient<ISttStreamingService, DeepgramStreamingService>();
        services.AddHttpClient<ITtsStreamingService, ElevenLabsStreamingService>();
        services.AddHttpClient<ILlmStreamingService, GeminiStreamingService>();

        return services;
    }
}
