using AiCallAssistent.API.Filters;
using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwilioSettings = AiCallAssistent.Application.Configuration.TwilioSettings;

namespace AiCallAssistent.API.Controllers;

/// <summary>
/// Inbound call flow: Twilio / Deepgram / Gemini / ElevenLabs.
/// Voice webhook: POST {Twilio:BaseUrl}/api/twilio/answer
/// Status callback: POST {Twilio:BaseUrl}/api/twilio/status
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
    private readonly ICallSetupService _callSetup;
    private readonly ICompanyPackageService _packageService;
    private readonly IBotScheduleService _botSchedule;
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly TwilioSettings _twilio;
    private readonly ILogger<TwilioController> _logger;

    public TwilioController(
        IElevenLabsService tts,
        IDeepgramService stt,
        IGeminiService gemini,
        IConversationStore conversations,
        IAudioStore audioStore,
        IHttpClientFactory httpClientFactory,
        ICallSetupService callSetup,
        ICompanyPackageService packageService,
        IBotScheduleService botSchedule,
        AppDbContext db,
        IOptions<TwilioSettings> twilioSettings,
        IWhatsAppService whatsApp,
        ILogger<TwilioController> logger)
    {
        _tts = tts;
        _stt = stt;
        _gemini = gemini;
        _conversations = conversations;
        _audioStore = audioStore;
        _httpClientFactory = httpClientFactory;
        _callSetup = callSetup;
        _packageService = packageService;
        _botSchedule = botSchedule;
        _db = db;
        _twilio = twilioSettings.Value;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    // ── Answer ───────────────────────────────────────────────────────────────

    [HttpPost("answer")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> Answer()
    {
        var callSid      = Request.Form["CallSid"].ToString();
        var calledNumber = Request.Form["To"].ToString();
        var callerNumber = Request.Form["From"].ToString();

        var setup = await _callSetup.LoadAsync(calledNumber, callerNumber);

        // Blacklist check.
        if (setup.Features.Blacklist)
        {
            try
            {
                var isBlacklisted = await _db.CallBlacklist
                    .AsNoTracking()
                    .AnyAsync(b => b.CompanyId == setup.CompanyId && b.PhoneNumber == callerNumber);

                if (isBlacklisted)
                {
                    _logger.LogInformation("Blacklisted caller {CallerNumber} blocked for {CallSid}", callerNumber, callSid);
                    try
                    {
                        _db.CallSessions.Add(new CallSession
                        {
                            CallSid       = callSid,
                            CompanyId     = setup.CompanyId,
                            PhoneNumber   = calledNumber,
                            CallerNumber  = callerNumber,
                            StartedAt     = DateTimeOffset.UtcNow,
                            Status        = "blacklisted",
                            CreatedAt     = DateTimeOffset.UtcNow
                        });
                        await _db.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist blacklisted CallSession for {CallSid}", callSid);
                    }

                    return TwimlResult("""<?xml version="1.0" encoding="UTF-8"?><Response><Reject/></Response>""");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blacklist check failed for {CallSid}", callSid);
            }
        }

        _conversations.Initialize(callSid,
            Application.Helpers.ConversationSeeder.BuildInitialTurns(
                setup.WelcomeText, setup.AppointmentTypesNode, setup.DepartmentsNode));

        try
        {
            _db.CallSessions.Add(new CallSession
            {
                CallSid      = callSid,
                CompanyId    = setup.CompanyId,
                PhoneNumber  = calledNumber,
                CallerNumber = callerNumber,
                StartedAt    = DateTimeOffset.UtcNow,
                Status       = "in-progress",
                CreatedAt    = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist CallSession for {CallSid}", callSid);
        }

        // Feature 1 — backup mode: try the escalation number first.
        if (setup.CallMode == CallMode.Backup && !setup.IsAfterHours && setup.EscalationNumber is { Length: > 0 } esc)
        {
            var backupActionUrl = $"{_twilio.BaseUrl}/api/twilio/backup-no-answer"
                + $"?calledNumber={Uri.EscapeDataString(calledNumber)}"
                + $"&callerNumber={Uri.EscapeDataString(callerNumber)}";

            return TwimlResult(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Dial timeout="20" action="{XmlEscape(backupActionUrl)}" callerId="{XmlEscape(callerNumber)}">
                        <Number>{XmlEscape(esc)}</Number>
                    </Dial>
                </Response>
                """);
        }

        return await StartBotFlowAsync(setup, calledNumber, callSid, callerNumber);
    }

    // ── Backup no-answer ─────────────────────────────────────────────────────

    [HttpPost("backup-no-answer")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> BackupNoAnswer(
        [FromQuery] string calledNumber = "",
        [FromQuery] string callerNumber = "")
    {
        var callSid    = Request.Form["CallSid"].ToString();
        var dialStatus = Request.Form["DialCallStatus"].ToString();

        // Human picked up — nothing left to do.
        if (dialStatus is "completed" or "answered")
            return TwimlResult("""<?xml version="1.0" encoding="UTF-8"?><Response><Hangup/></Response>""");

        // Human didn't answer — hand off to the bot.
        var setup = await _callSetup.LoadAsync(calledNumber, callerNumber);
        return await StartBotFlowAsync(setup, calledNumber, callSid, callerNumber);
    }

    // ── Recording ────────────────────────────────────────────────────────────

    [HttpPost("recording")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult Recording() => Ok(); // Stale webhooks — streaming pipeline no longer uses this

    // ── Audio ────────────────────────────────────────────────────────────────

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

    // ── Status ───────────────────────────────────────────────────────────────

    /// <summary>Must return 200 — Twilio retries on non-2xx.</summary>
    [HttpPost("status")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> Status()
    {
        var callSid    = Request.Form["CallSid"].ToString();
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

            var summary        = await _gemini.SummarizeConversationAsync(callSid);
            var classification = await _gemini.ClassifyCallerAsync(callSid);

            session.EndedAt              = DateTimeOffset.UtcNow;
            session.Status               = "completed";
            session.Summary              = string.IsNullOrWhiteSpace(summary) ? null : summary;
            session.CallerClassification = classification;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Call {CallSid} completed. Classification: {Class}. Summary: {Summary}",
                callSid, classification, session.Summary ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status callback failed for {CallSid}", callSid);
        }

        return Ok();
    }

    // ── Dial status ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called by Twilio after a &lt;Dial&gt; completes. If the department did not answer,
    /// dials the fallback number instead.
    /// </summary>
    [HttpPost("dial-status")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public IActionResult DialStatus([FromQuery] string? fallback)
    {
        var dialStatus = Request.Form["DialCallStatus"].ToString();

        if (fallback is { Length: > 0 } && dialStatus is "no-answer" or "busy" or "failed" or "canceled")
        {
            return TwimlResult($"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Dial>{XmlEscape(fallback)}</Dial>
                </Response>
                """);
        }

        return TwimlResult("""<?xml version="1.0" encoding="UTF-8"?><Response></Response>""");
    }

    // ── After-hours no-answer ────────────────────────────────────────────────

    [HttpPost("after-hours-no-answer")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> AfterHoursNoAnswer([FromQuery] string calledNumber = "")
    {
        var callSid      = Request.Form["CallSid"].ToString();
        var callerNumber = Request.Form["From"].ToString();
        var dialStatus   = Request.Form["DialCallStatus"].ToString();

        if (dialStatus is "completed" or "answered")
            return TwimlResult("""<?xml version="1.0" encoding="UTF-8"?><Response></Response>""");

        try
        {
            // Do NOT call StartBotFlowAsync — it would re-check IsAfterHours&&TryHuman and dial again.
            // Build stream TwiML directly so the bot picks up after the failed escalation dial.
            return TwimlResult(BuildStreamTwiml(calledNumber, callerNumber, afterHoursNoAnswer: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in AfterHoursNoAnswer for {CalledNumber}", calledNumber);
            return TwimlResult("""<?xml version="1.0" encoding="UTF-8"?><Response><Hangup/></Response>""");
        }
    }

    // ── Shared bot startup (Features 1 & 3) ─────────────────────────────────

    /// <summary>
    /// Runs the bot-entry TwiML: checks bot-active-hours, checks after-hours mode,
    /// then returns a &lt;Connect&gt;&lt;Stream&gt; WebSocket TwiML for the streaming pipeline.
    /// Called from Answer(), BackupNoAnswer(), and AfterHoursNoAnswer().
    /// </summary>
    private async Task<IActionResult> StartBotFlowAsync(
        CallSetupData setup, string calledNumber, string callSid, string callerNumber)
    {
        // Feature 3 — bot active hours.
        if (setup.BotActiveHoursEnabled)
        {
            try
            {
                var isBotActive = await _botSchedule.IsBotActiveNowAsync(setup.CompanyId, setup.ActiveProfileId);
                if (!isBotActive)
                {
                    _logger.LogInformation(
                        "Bot outside active hours for company {CompanyId} — transferring or hanging up",
                        setup.CompanyId);

                    if (setup.EscalationNumber is { Length: > 0 } esc)
                        return TwimlResult(
                            $"""
                            <?xml version="1.0" encoding="UTF-8"?>
                            <Response>
                                <Say language="nl-NL">De assistent is momenteel niet beschikbaar. U wordt doorverbonden.</Say>
                                <Dial>{XmlEscape(esc)}</Dial>
                            </Response>
                            """);

                    return TwimlResult(
                        """
                        <?xml version="1.0" encoding="UTF-8"?>
                        <Response>
                            <Say language="nl-NL">De assistent is momenteel niet beschikbaar. Probeer het later opnieuw.</Say>
                            <Hangup/>
                        </Response>
                        """);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bot-active-hours check failed for company {CompanyId}", setup.CompanyId);
            }
        }

        // After-hours: try-human path — dial escalation first; streaming bot starts on no-answer.
        if (setup.IsAfterHours
            && setup.AfterHoursMode == AfterHoursMode.TryHuman
            && setup.EscalationNumber is { Length: > 0 })
        {
            var noAnswerUrl = $"{_twilio.BaseUrl}/api/twilio/after-hours-no-answer"
                + $"?calledNumber={Uri.EscapeDataString(calledNumber)}";
            return await SpeakAndAfterHoursDialAsync(setup.WelcomeText, setup.EscalationNumber, noAnswerUrl, setup.Language);
        }

        return TwimlResult(BuildStreamTwiml(calledNumber, callerNumber));
    }

    private string BuildStreamTwiml(string calledNumber, string callerNumber, bool afterHoursNoAnswer = false)
    {
        var wsUrl = _twilio.BaseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            + "/ws/twilio";

        var extraParam = afterHoursNoAnswer
            ? $"\n                    <Parameter name=\"noAnswer\" value=\"1\"/>"
            : "";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
                <Connect>
                    <Stream url="{XmlEscape(wsUrl)}">
                        <Parameter name="calledNumber" value="{XmlEscape(calledNumber)}"/>
                        <Parameter name="callerNumber" value="{XmlEscape(callerNumber)}"/>{extraParam}
                    </Stream>
                </Connect>
            </Response>
            """;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<ContentResult> SpeakAndRecordAsync(string text, string recordingActionUrl, string? language = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language);
            var id = _audioStore.Store(audioBytes, "audio/mpeg");
            var twiml = BuildTwiml($"{_twilio.BaseUrl}/api/twilio/audio/{id}", recordingActionUrl);
            _logger.LogInformation("Returning TwiML: {Twiml}", twiml);
            return TwimlResult(twiml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis failed — falling back to <Say>: {Preview}",
                text.Length > 80 ? text[..80] + "…" : text);
            return TwimlResult(BuildFallbackTwiml(text, recordingActionUrl));
        }
    }

    private async Task<ContentResult> SpeakAndAfterHoursDialAsync(string text, string dialNumber, string noAnswerUrl, string? language = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language);
            var id = _audioStore.Store(audioBytes, "audio/mpeg");
            return TwimlResult(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Play>{_twilio.BaseUrl}/api/twilio/audio/{id}</Play>
                    <Dial action="{XmlEscape(noAnswerUrl)}" timeout="25"><Number>{XmlEscape(dialNumber)}</Number></Dial>
                </Response>
                """);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis failed during after-hours dial — falling back to <Say>");
            return TwimlResult(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Say language="nl-NL">{XmlEscape(text)}</Say>
                    <Dial action="{XmlEscape(noAnswerUrl)}" timeout="25"><Number>{XmlEscape(dialNumber)}</Number></Dial>
                </Response>
                """);
        }
    }

    private async Task<ContentResult> SpeakAndDialAsync(string text, string dialNumber, string? fallbackNumber = null, string? language = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language);
            var id = _audioStore.Store(audioBytes, "audio/mpeg");
            return TwimlResult(BuildDialTwiml($"{_twilio.BaseUrl}/api/twilio/audio/{id}", dialNumber, fallbackNumber));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis failed during escalation — falling back to <Say>");
            return TwimlResult(BuildFallbackDialTwiml(text, dialNumber, fallbackNumber));
        }
    }

    /// <summary>Feature 2 — returns a graceful error TwiML that transfers or hangs up.</summary>
    private static ContentResult ErrorTransferTwiml(string? escalationNumber, string language = "nl")
    {
        if (escalationNumber is { Length: > 0 })
            return TwimlResult(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Say language="nl-NL">Er is een technisch probleem opgetreden. U wordt nu doorverbonden.</Say>
                    <Dial>{XmlEscape(escalationNumber)}</Dial>
                </Response>
                """);

        return TwimlResult(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
                <Say language="nl-NL">Er is een technisch probleem opgetreden. Probeer het later opnieuw.</Say>
                <Hangup/>
            </Response>
            """);
    }

    private static string BuildTwiml(string audioUrl, string recordingActionUrl) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
            <Play>{audioUrl}</Play>
            <Record action="{XmlEscape(recordingActionUrl)}" maxLength="60" timeout="3" playBeep="false" />
        </Response>
        """;

    private string BuildDialTwiml(string audioUrl, string dialNumber, string? fallbackNumber = null)
    {
        var dialTag = fallbackNumber is { Length: > 0 }
            ? $"""<Dial action="{_twilio.BaseUrl}/api/twilio/dial-status?fallback={Uri.EscapeDataString(fallbackNumber)}" timeout="25"><Number>{XmlEscape(dialNumber)}</Number></Dial>"""
            : $"<Dial>{XmlEscape(dialNumber)}</Dial>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
                <Play>{audioUrl}</Play>
                {dialTag}
            </Response>
            """;
    }

    private static string BuildFallbackTwiml(string text, string recordingActionUrl) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
            <Say language="nl-NL">{XmlEscape(text)}</Say>
            <Record action="{XmlEscape(recordingActionUrl)}" maxLength="60" timeout="3" playBeep="false" />
        </Response>
        """;

    private string BuildFallbackDialTwiml(string text, string dialNumber, string? fallbackNumber = null)
    {
        var dialTag = fallbackNumber is { Length: > 0 }
            ? $"""<Dial action="{_twilio.BaseUrl}/api/twilio/dial-status?fallback={Uri.EscapeDataString(fallbackNumber)}" timeout="25"><Number>{XmlEscape(dialNumber)}</Number></Dial>"""
            : $"<Dial>{XmlEscape(dialNumber)}</Dial>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
                <Say language="nl-NL">{XmlEscape(text)}</Say>
                {dialTag}
            </Response>
            """;
    }

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── Incoming WhatsApp ────────────────────────────────────────────────────

    /// <summary>
    /// Twilio posts here when a customer sends a WhatsApp message to the company's number.
    /// Configure "When a message comes in" in the Twilio console:
    ///   POST {Twilio:BaseUrl}/api/twilio/whatsapp
    /// </summary>
    [HttpPost("whatsapp")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateTwilioRequest]
    public async Task<IActionResult> WhatsAppIncoming()
    {
        var from = Request.Form["From"].ToString(); // "whatsapp:+31612345678"
        var to   = Request.Form["To"].ToString();   // "whatsapp:+14155238886"
        var body = Request.Form["Body"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(body))
            return TwimlResult("<Response/>");

        var fromNumber = from.Replace("whatsapp:", "", StringComparison.OrdinalIgnoreCase);
        var toNumber   = to.Replace("whatsapp:", "", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("WhatsApp message from {From}: {Body}", fromNumber, body);

        try
        {
            var setup = await _callSetup.LoadAsync(toNumber, fromNumber);

            var conversationKey = $"whatsapp:{fromNumber}:{setup.CompanyId}";

            _conversations.Initialize(conversationKey,
                Application.Helpers.ConversationSeeder.BuildInitialTurns(
                    setup.WelcomeText, setup.AppointmentTypesNode, setup.DepartmentsNode));

            var context = new CallDispatchContext(
                setup.CompanyId,
                fromNumber,
                setup.EscalationNumber,
                setup.DepartmentPhones,
                setup.Branch,
                setup.Features);

            var config = new CompanyCallConfig(
                setup.SystemPrompt,
                setup.Language,
                GreetingMessage: null,
                AfterHoursMode: null,
                IsWhatsApp: true);

            var result = await _gemini.RunConversationAsync(context, body, conversationKey, config);

            var replyText = result.Success && !string.IsNullOrWhiteSpace(result.FinalResponse)
                ? result.FinalResponse
                : "Sorry, er is iets misgegaan. Probeer het later opnieuw.";

            return TwimlResult(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Message>{XmlEscape(replyText)}</Message>
                </Response>
                """);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp incoming failed for {From}", fromNumber);
            return TwimlResult(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Message>Sorry, er is een technisch probleem. Probeer het later opnieuw.</Message>
                </Response>
                """);
        }
    }

    private static ContentResult TwimlResult(string twiml) =>
        new() { Content = twiml, ContentType = "application/xml", StatusCode = 200 };
}
