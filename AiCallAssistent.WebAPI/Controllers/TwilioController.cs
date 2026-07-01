using System.Text;
using System.Text.Json.Nodes;
using AiCallAssistent.WebAPI.Filters;
using AiCallAssistent.Application.Constants;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TwilioSettings = AiCallAssistent.Application.Configuration.TwilioSettings;

namespace AiCallAssistent.WebAPI.Controllers;

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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

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
        ILogger<TwilioController> logger,
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache)
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
        _scopeFactory = scopeFactory;
        _cache = cache;
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

        // Subscription lockout — pass the call straight through to escalation number.
        if (setup.SubscriptionLocked)
        {
            _logger.LogInformation("Subscription locked for company {CompanyId} — passing through {CallSid}",
                setup.CompanyId, callSid);
            if (setup.EscalationNumber is { Length: > 0 } passthrough)
            {
                return TwimlResult(
                    $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <Response>
                        <Dial callerId="{XmlEscape(callerNumber)}">
                            <Number>{XmlEscape(passthrough)}</Number>
                        </Dial>
                    </Response>
                    """);
            }
            return await SpeakAndHangupAsync(
                "Onze telefonische assistent is momenteel niet beschikbaar. Probeer het later opnieuw.",
                setup.Language, setup.VoiceKey);
        }

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

            var turns = _conversations.Load(callSid);
            var transcript = BuildTranscript(turns);

            var callType = _conversations.GetCallOutcome(callSid) ?? "Info";

            var durationSec = (int)(DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds;

            string? callerName = null;
            if (!string.IsNullOrWhiteSpace(session.CallerNumber))
            {
                callerName = await _db.CallbackRequests
                    .Where(r => r.CompanyId == session.CompanyId && r.CallerNumber == session.CallerNumber)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => (string?)r.CallerName)
                    .FirstOrDefaultAsync();
            }

            session.EndedAt              = DateTimeOffset.UtcNow;
            session.Status               = "completed";
            session.Summary              = string.IsNullOrWhiteSpace(summary) ? null : summary;
            session.CallerClassification = classification;
            session.CallType             = callType;
            session.Transcript           = string.IsNullOrWhiteSpace(transcript) ? null : transcript;
            session.CallerName           = callerName;
            session.DurationSeconds      = durationSec;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Call {CallSid} completed. Classification: {Class}. Type: {Type}. Summary: {Summary}",
                callSid, classification, callType, session.Summary ?? "(none)");

            if (!string.IsNullOrWhiteSpace(session.Summary))
            {
                var summarySnapshot = session.Summary;
                var companyId = session.CompanyId;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var suggestions = await _gemini.GenerateKnowledgeSuggestionsAsync(summarySnapshot);
                        if (suggestions.Count > 0)
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            foreach (var text in suggestions)
                                scopedDb.KnowledgeSuggestions.Add(new KnowledgeSuggestion
                                {
                                    CompanyId = companyId,
                                    Text = text,
                                    Status = "new",
                                    CreatedAt = DateTimeOffset.UtcNow
                                });
                            await scopedDb.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Knowledge suggestion generation failed for {CallSid}", callSid);
                    }
                });
            }
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
        var dialStatus   = Request.Form["DialCallStatus"].ToString();
        var calledNumber = Request.Form["Called"].ToString();
        var callerNumber = Request.Form["From"].ToString();

        if (fallback is { Length: > 0 } && dialStatus is "no-answer" or "busy" or "failed" or "canceled")
        {
            // Try the fallback number; on no-answer it will hit dial-status again (without fallback) → bot resumes
            return TwimlResult($"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Dial action="{_twilio.BaseUrl}/api/twilio/dial-status" timeout="25"><Number>{XmlEscape(fallback)}</Number></Dial>
                </Response>
                """);
        }

        // Transfer went unanswered — resume bot so it can help plan a callback
        if (dialStatus is "no-answer" or "busy" or "failed" or "canceled")
        {
            _logger.LogInformation("Transfer unanswered for {CalledNumber} — resuming bot for callback", calledNumber);
            return TwimlResult(BuildStreamTwiml(calledNumber, callerNumber, transferNoAnswer: true));
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
                        return await SpeakAndDialAsync(
                            "De assistent is momenteel niet beschikbaar. U wordt doorverbonden.",
                            esc, language: setup.Language, voiceKey: setup.VoiceKey);

                    return await SpeakAndHangupAsync(
                        "De assistent is momenteel niet beschikbaar. Probeer het later opnieuw.",
                        setup.Language, setup.VoiceKey);
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
            return await SpeakAndAfterHoursDialAsync(setup.WelcomeText, setup.EscalationNumber, noAnswerUrl, setup.Language, setup.VoiceKey);
        }

        var streamTwiml = BuildStreamTwiml(calledNumber, callerNumber);
        _logger.LogInformation("Returning Stream TwiML for {CallSid}: {TwimlPreview}", callSid, streamTwiml[..Math.Min(200, streamTwiml.Length)]);
        return TwimlResult(streamTwiml);
    }

    // ── Transfer action callback ─────────────────────────────────────────────
    // Called by Twilio after <Connect> ends (WebSocket closed by CallStreamHandler).
    // If a transfer was queued in IMemoryCache, return <Dial> TwiML; otherwise hang up.

    [HttpPost("transfer")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult Transfer()
    {
        var callSid = Request.Form["CallSid"].ToString();

        if (_cache.TryGetValue($"transfer_{callSid}", out string? entry) && entry is { Length: > 0 })
        {
            _cache.Remove($"transfer_{callSid}");
            var parts    = entry.Split('|');
            var number   = parts[0];
            var fallback = parts.Length > 1 ? parts[1] : null;
            // Always include action URL so Twilio calls back on no-answer → bot resumes for callback
            var actionUrl = fallback is { Length: > 0 }
                ? $"{_twilio.BaseUrl}/api/twilio/dial-status?fallback={Uri.EscapeDataString(fallback)}"
                : $"{_twilio.BaseUrl}/api/twilio/dial-status";
            var dialTag = $"""<Dial action="{actionUrl}" timeout="25"><Number>{XmlEscape(number)}</Number></Dial>""";
            var twiml = $"""<?xml version="1.0" encoding="UTF-8"?><Response>{dialTag}</Response>""";

            _logger.LogInformation("Transfer action triggered for {CallSid} → {Number}", callSid, number);
            return TwimlResult(twiml);
        }

        _logger.LogDebug("Transfer action for {CallSid} — no pending transfer, hanging up", callSid);
        return TwimlResult("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>");
    }

    private string BuildStreamTwiml(string calledNumber, string callerNumber, bool afterHoursNoAnswer = false, bool transferNoAnswer = false)
    {
        var wsUrl = _twilio.BaseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            + "/ws/twilio";

        var extraParam = afterHoursNoAnswer
            ? $"\n                    <Parameter name=\"noAnswer\" value=\"1\"/>"
            : transferNoAnswer
                ? $"\n                    <Parameter name=\"transferNoAnswer\" value=\"1\"/>"
                : "";

        var transferActionUrl = $"{_twilio.BaseUrl}/api/twilio/transfer";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
                <Connect action="{XmlEscape(transferActionUrl)}">
                    <Stream url="{XmlEscape(wsUrl)}">
                        <Parameter name="calledNumber" value="{XmlEscape(calledNumber)}"/>
                        <Parameter name="callerNumber" value="{XmlEscape(callerNumber)}"/>{extraParam}
                    </Stream>
                </Connect>
            </Response>
            """;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<ContentResult> SpeakAndRecordAsync(string text, string recordingActionUrl, string? language = null, string? voiceKey = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language, voiceKey);
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

    private async Task<ContentResult> SpeakAndAfterHoursDialAsync(string text, string dialNumber, string noAnswerUrl, string? language = null, string? voiceKey = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language, voiceKey);
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

    private async Task<ContentResult> SpeakAndDialAsync(string text, string dialNumber, string? fallbackNumber = null, string? language = null, string? voiceKey = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language, voiceKey);
            var id = _audioStore.Store(audioBytes, "audio/mpeg");
            return TwimlResult(BuildDialTwiml($"{_twilio.BaseUrl}/api/twilio/audio/{id}", dialNumber, fallbackNumber));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis failed during escalation — falling back to <Say>");
            return TwimlResult(BuildFallbackDialTwiml(text, dialNumber, fallbackNumber));
        }
    }

    private async Task<ContentResult> SpeakAndHangupAsync(string text, string? language = null, string? voiceKey = null)
    {
        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, language, voiceKey);
            var id = _audioStore.Store(audioBytes, "audio/mpeg");
            return TwimlResult(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Play>{_twilio.BaseUrl}/api/twilio/audio/{id}</Play>
                    <Hangup/>
                </Response>
                """);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis failed during hangup — falling back to <Say>");
            return TwimlResult(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                    <Say language="nl-NL">{XmlEscape(text)}</Say>
                    <Hangup/>
                </Response>
                """);
        }
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
                GreetingMessage:      null,
                AfterHoursMode:       null,
                IsWhatsApp:           true,
                AssistantName:        setup.AssistantName,
                Tone:                 setup.Tone,
                AutoTimeGreeting:     setup.AutoTimeGreeting,
                UseCallerName:        setup.UseCallerName,
                TopicsYes:            setup.TopicsYes,
                TopicsNo:             setup.TopicsNo,
                FallbackBehavior:     setup.FallbackBehavior,
                BehaviorInstructions: setup.BehaviorInstructions,
                RoutingRulesJson:     setup.RoutingRulesJson);

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

    private static string BuildTranscript(JsonArray turns)
    {
        var sb = new StringBuilder();
        foreach (var turn in turns)
        {
            if (turn is not JsonObject obj) continue;
            var role = obj["role"]?.GetValue<string>();
            if (role is not ("user" or "model")) continue;

            var parts = obj["parts"]?.AsArray();
            if (parts is null) continue;

            foreach (var part in parts)
            {
                if (part?["text"] is not JsonNode textNode) continue;
                var text = textNode.GetValue<string>().Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                var label = role == "model" ? "Assistent" : "Beller";
                sb.AppendLine($"{label}: {text}");
            }
        }
        return sb.ToString().Trim();
    }

    private static ContentResult TwimlResult(string twiml) =>
        new() { Content = twiml, ContentType = "application/xml", StatusCode = 200 };
}
