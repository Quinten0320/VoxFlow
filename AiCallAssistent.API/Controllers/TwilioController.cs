using System.Text.Json;
using System.Text.Json.Nodes;
using AiCallAssistent.API.Filters;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using AssistantSettings = AiCallAssistent.Application.Configuration.AssistantSettings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.API.Controllers;

/// <summary>
/// Handles the full inbound call flow: Twilio → Deepgram → Gemini → ElevenLabs → Twilio.
///
/// Setup:
///   1. Set Twilio:BaseUrl to your public URL (ngrok in dev).
///   2. Point the Twilio number's Voice webhook to POST {BaseUrl}/api/twilio/answer.
///   3. Point the Status Callback to POST {BaseUrl}/api/twilio/status.
///   4. Add Twilio:AccountSid + Twilio:AuthToken to User Secrets.
/// </summary>
[ApiController]
[Route("api/twilio")]
public class TwilioController : ControllerBase
{
    private readonly IElevenLabsService _tts;
    private readonly IDeepgramService _stt;
    private readonly IGeminiService _gemini;
    private readonly IConversationStore _conversations;
    private readonly IAudioStore _audioStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppointmentService _appointments;
    private readonly TwilioSettings _twilio;
    private readonly AssistantSettings _assistant;
    private readonly AppDbContext _db;
    private readonly ILogger<TwilioController> _logger;

    public TwilioController(
        IElevenLabsService tts,
        IDeepgramService stt,
        IGeminiService gemini,
        IConversationStore conversations,
        IAudioStore audioStore,
        IHttpClientFactory httpClientFactory,
        IAppointmentService appointments,
        IOptions<TwilioSettings> twilioSettings,
        IOptions<AssistantSettings> assistantSettings,
        AppDbContext db,
        ILogger<TwilioController> logger)
    {
        _tts = tts;
        _stt = stt;
        _gemini = gemini;
        _conversations = conversations;
        _audioStore = audioStore;
        _httpClientFactory = httpClientFactory;
        _appointments = appointments;
        _twilio = twilioSettings.Value;
        _assistant = assistantSettings.Value;
        _db = db;
        _logger = logger;
    }

    [HttpPost("answer")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> Answer()
    {
        var callSid = Request.Form["CallSid"].ToString();
        var calledNumber = Request.Form["To"].ToString();
        var callerNumber = Request.Form["From"].ToString();

        short companyId = _assistant.DefaultCompanyId;
        string? escalationNumber = null;
        try
        {
            var phoneRow = await _db.PhoneNumbers
                .AsNoTracking()
                .Where(p => p.AiPhoneNumber == calledNumber && p.IsActive)
                .Select(p => new { p.CompanyId, p.EscalationPhoneNumber })
                .FirstOrDefaultAsync();

            if (phoneRow != null)
            {
                companyId = phoneRow.CompanyId;
                escalationNumber = phoneRow.EscalationPhoneNumber;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not look up phone number {CalledNumber} for {CallSid}", calledNumber, callSid);
        }

        Domain.Models.AssistantSettings? assistantSettings = null;
        try
        {
            assistantSettings = await _db.AssistantSettings
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not load assistant settings for company {CompanyId} ({CallSid})", companyId, callSid);
        }

        var language = assistantSettings?.Language is { Length: > 0 } l ? l : "nl";
        var recordingActionUrl = $"{_twilio.BaseUrl}/api/twilio/recording?companyId={companyId}";

        string welcomeText;
        if (assistantSettings?.GreetingsMessage is { Length: > 0 } dbGreeting)
        {
            welcomeText = dbGreeting;
        }
        else
        {
            var companyName = "ons bedrijf";
            try
            {
                var name = await _db.Companies
                    .Where(c => c.CompanyId == companyId)
                    .Select(c => c.CompanyName)
                    .FirstOrDefaultAsync();
                if (name is { Length: > 0 }) companyName = name;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load company name for {CompanyId} ({CallSid})", companyId, callSid);
            }
            welcomeText = _assistant.WelcomeMessage.Replace("{company}", companyName);
        }

        JsonNode? appointmentTypesNode = null;
        try
        {
            var types = await _appointments.GetAppointmentTypesAsync(companyId);
            appointmentTypesNode = JsonSerializer.SerializeToNode(types);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not pre-fetch appointment types for {CallSid}", callSid);
        }

        _conversations.Initialize(callSid,
            ConversationSeeder.BuildInitialTurns(welcomeText, appointmentTypesNode));

        try
        {
            _db.CallSessions.Add(new CallSession
            {
                CallSid = callSid,
                CompanyId = companyId,
                PhoneNumber = calledNumber,
                CallerNumber = callerNumber,
                StartedAt = DateTimeOffset.UtcNow,
                Status = "in-progress",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist CallSession for {CallSid}", callSid);
        }

        return await SpeakAndRecordAsync(welcomeText, recordingActionUrl, language);
    }

    [HttpPost("recording")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> Recording([FromQuery] short companyId)
    {
        var callSid = Request.Form["CallSid"].ToString();
        var callerNumber = Request.Form["From"].ToString();
        var recordingUrl = Request.Form["RecordingUrl"].ToString();
        var recordingStatus = Request.Form["RecordingStatus"].ToString();
        var recordingDuration = int.TryParse(Request.Form["RecordingDuration"], out var d) ? d : 0;

        string language = "nl";
        string? escalationNumber = null;
        string? systemPrompt = null;
        try
        {
            var settings = await _db.AssistantSettings
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .FirstOrDefaultAsync();

            if (settings != null)
            {
                if (settings.Language is { Length: > 0 } l) language = l;
                if (settings.Prompt is { Length: > 0 } p) systemPrompt = p;
            }

            escalationNumber = await _db.PhoneNumbers
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.IsActive)
                .Select(p => p.EscalationPhoneNumber)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not load settings for company {CompanyId} ({CallSid})", companyId, callSid);
        }

        var recordingActionUrl = $"{_twilio.BaseUrl}/api/twilio/recording?companyId={companyId}";

        if (recordingStatus != "completed" || recordingDuration == 0)
            return await SpeakAndRecordAsync(
                "Sorry, ik heb u niet gehoord. Kunt u dat herhalen?", recordingActionUrl, language);

        byte[] audioBytes;
        try
        {
            var client = _httpClientFactory.CreateClient("Twilio");
            var mp3Response = await client.GetAsync(recordingUrl + ".mp3");
            mp3Response.EnsureSuccessStatusCode();
            audioBytes = await mp3Response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download recording for {CallSid} — {Url}", callSid, recordingUrl);
            return await SpeakAndRecordAsync(
                "Er is een technisch probleem opgetreden. Probeert u het opnieuw.", recordingActionUrl, language);
        }

        string transcript;
        try
        {
            transcript = await _stt.TranscribeAsync(audioBytes, "audio/mpeg", language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deepgram transcription failed for {CallSid}", callSid);
            return await SpeakAndRecordAsync(
                "Er is een technisch probleem opgetreden. Probeert u het opnieuw.", recordingActionUrl, language);
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            _logger.LogInformation("Empty transcript for {CallSid}", callSid);
            return await SpeakAndRecordAsync(
                "Sorry, ik kon u niet verstaan. Kunt u dat herhalen?", recordingActionUrl, language);
        }

        _logger.LogInformation("Transcript for {CallSid}: {CharCount} chars", callSid, transcript.Length);

        var context = new CallDispatchContext(companyId, callerNumber, escalationNumber);
        var config = new CompanyCallConfig(systemPrompt, language, null);
        var geminiResult = await _gemini.RunConversationAsync(context, transcript, callSid, config);

        if (!geminiResult.Success)
            _logger.LogError("Gemini failed for {CallSid}: {Error}", callSid, geminiResult.Error);

        var replyText = geminiResult.Success
            ? geminiResult.FinalResponse
            : "Er is een fout opgetreden. Probeert u het straks opnieuw.";

        if (geminiResult.EscalationNumber is { Length: > 0 } dialNumber)
            return await SpeakAndDialAsync(replyText, dialNumber, language);

        return await SpeakAndRecordAsync(replyText, recordingActionUrl, language);
    }

    [HttpGet("audio/{id}")]
    public IActionResult Audio(string id)
    {
        var result = _audioStore.Get(id);
        if (result is null)
        {
            _logger.LogWarning("Audio {Id} not found — likely expired", id);
            return NotFound();
        }
        return File(result.Value.Audio, result.Value.ContentType);
    }

    /// <summary>
    /// Twilio calls this when the call ends. Generates a summary and saves it.
    /// Must return 200 — Twilio retries on non-2xx.
    /// </summary>
    [HttpPost("status")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> Status()
    {
        var callSid = Request.Form["CallSid"].ToString();
        var callStatus = Request.Form["CallStatus"].ToString();

        if (callStatus != "completed") return Ok();

        try
        {
            var session = await _db.CallSessions.FindAsync(callSid);
            if (session is null)
            {
                _logger.LogWarning("Status callback for unknown CallSid {CallSid}", callSid);
                return Ok();
            }

            if (session.Summary is not null) return Ok(); // already processed

            var summary = await _gemini.SummarizeConversationAsync(callSid);

            session.EndedAt = DateTimeOffset.UtcNow;
            session.Status = "completed";
            session.Summary = string.IsNullOrWhiteSpace(summary) ? null : summary;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Call {CallSid} completed. Summary: {Summary}", callSid, session.Summary ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status callback failed for {CallSid}", callSid);
        }

        return Ok();
    }

    private async Task<ContentResult> SpeakAndRecordAsync(string text, string recordingActionUrl, string? language = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language);
            var id = _audioStore.Store(audioBytes, "audio/mpeg");
            return TwimlResult(BuildTwiml($"{_twilio.BaseUrl}/api/twilio/audio/{id}", recordingActionUrl));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis failed — falling back to <Say>: {Preview}",
                text.Length > 80 ? text[..80] + "…" : text);
            return TwimlResult(BuildFallbackTwiml(text, recordingActionUrl));
        }
    }

    private async Task<ContentResult> SpeakAndDialAsync(string text, string dialNumber, string? language = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language);
            var id = _audioStore.Store(audioBytes, "audio/mpeg");
            return TwimlResult(BuildDialTwiml($"{_twilio.BaseUrl}/api/twilio/audio/{id}", dialNumber));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis failed during escalation — falling back to <Say>");
            return TwimlResult(BuildFallbackDialTwiml(text, dialNumber));
        }
    }

    private static string BuildTwiml(string audioUrl, string recordingActionUrl) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
            <Play>{audioUrl}</Play>
            <Record action="{recordingActionUrl}" maxLength="60" timeout="3" playBeep="false" />
        </Response>
        """;

    private static string BuildDialTwiml(string audioUrl, string dialNumber) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
            <Play>{audioUrl}</Play>
            <Dial>{XmlEscape(dialNumber)}</Dial>
        </Response>
        """;

    private static string BuildFallbackTwiml(string text, string recordingActionUrl) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
            <Say language="nl-NL">{XmlEscape(text)}</Say>
            <Record action="{recordingActionUrl}" maxLength="60" timeout="3" playBeep="false" />
        </Response>
        """;

    private static string BuildFallbackDialTwiml(string text, string dialNumber) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
            <Say language="nl-NL">{XmlEscape(text)}</Say>
            <Dial>{XmlEscape(dialNumber)}</Dial>
        </Response>
        """;

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static ContentResult TwimlResult(string twiml) =>
        new() { Content = twiml, ContentType = "application/xml", StatusCode = 200 };
}
