using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.WebAPI.WebSockets;

public static class TwilioStreamEndpoint
{
    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            // Twilio Media Streams sends a WebSocket upgrade to /ws/twilio.
            // If this fires, something is sending a plain HTTP request here instead.
            var fallbackLogger = context.RequestServices.GetRequiredService<ILogger<CallStreamHandler>>();
            fallbackLogger.LogWarning("Non-WebSocket request to /ws/twilio — returning 400");
            context.Response.StatusCode = 400;
            return;
        }

        var ws = await context.WebSockets.AcceptWebSocketAsync();

        await using var scope = context.RequestServices.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<CallStreamHandler>>();
        logger.LogInformation("WebSocket accepted for /ws/twilio");

        try
        {
            // Twilio sends 'connected' first, then 'start'. Loop until we find 'start'.
            // callSid, calledNumber, callerNumber arrive in 'start' — NOT in query params.
            var buffer = new byte[8192];
            JsonNode? startRoot = null;
            for (var i = 0; i < 5 && startRoot?["event"]?.GetValue<string>() != "start"; i++)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
                startRoot = JsonNode.Parse(sb.ToString());
            }
            var callSid     = startRoot?["start"]?["callSid"]?.GetValue<string>() ?? "";
            var streamSid   = startRoot?["streamSid"]?.GetValue<string>() ?? "";
            var customParams = startRoot?["start"]?["customParameters"];
            var calledNumber = customParams?["calledNumber"]?.GetValue<string>() ?? "";
            var callerNumber = customParams?["callerNumber"]?.GetValue<string>() ?? "";
            var noAnswer         = customParams?["noAnswer"]?.GetValue<string>() == "1";
            var transferNoAnswer = customParams?["transferNoAnswer"]?.GetValue<string>() == "1";

            logger.LogInformation("Stream start: callSid={CallSid} calledNumber={CalledNumber} noAnswer={NoAnswer} transferNoAnswer={TransferNoAnswer}",
                callSid, calledNumber, noAnswer, transferNoAnswer);

            if (string.IsNullOrEmpty(callSid))
            {
                logger.LogWarning("Twilio start message missing callSid — closing WebSocket");
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "missing callSid", CancellationToken.None);
                return;
            }

            // Security: verify callSid was registered by Answer()
            var db = sp.GetRequiredService<AppDbContext>();
            var sessionExists = await db.CallSessions.AnyAsync(s => s.CallSid == callSid);
            if (!sessionExists)
            {
                logger.LogWarning("Unknown callSid {CallSid} — closing WebSocket", callSid);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "unknown callSid", CancellationToken.None);
                return;
            }

            var callSetup = sp.GetRequiredService<ICallSetupService>();
            var setup = await callSetup.LoadAsync(calledNumber, callerNumber);

            // After-hours no-answer: escalation didn't pick up, bot takes over with appropriate greeting
            if (noAnswer)
                setup = setup with { WelcomeText = "Helaas is niemand beschikbaar. Ik kan u helpen een terugbelverzoek in te plannen." };

            // Transfer no-answer: the person being called didn't pick up; bot resumes to take a callback request
            if (transferNoAnswer)
                setup = setup with { WelcomeText = "De lijn was helaas bezet of er werd niet opgenomen. Ik kan u helpen een terugbelverzoek in te plannen, zodat u zo snel mogelijk wordt teruggebeld." };

            var deepgram      = sp.GetRequiredService<ISttStreamingService>();
            var elevenlabs    = sp.GetRequiredService<ITtsStreamingService>();
            var gemini        = sp.GetRequiredService<ILlmStreamingService>();
            var twilioSettings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var cache         = sp.GetRequiredService<IMemoryCache>();

            var handler = new CallStreamHandler(
                ws, setup, callSid, calledNumber, callerNumber,
                deepgram, gemini, elevenlabs,
                twilioSettings, cache, logger,
                streamSid: streamSid);

            using var cts = new CancellationTokenSource();
            await handler.RunAsync(cts.Token);

            // Persist STT transcription confidence before disposing
            var avgConfidence = deepgram.AverageConfidence;

            // Per-call summary so Flux / Nova-3 / Scribe can be compared across deployments:
            // filter logs on "[STT] call summary" (or "[STT] provider=" for per-turn lines).
            var deepgramSettings  = sp.GetRequiredService<IOptions<DeepgramSettings>>().Value;
            var elevenLabsSettings = sp.GetRequiredService<IOptions<ElevenLabsSettings>>().Value;
            var sttProvider = deepgramSettings.SttProvider;
            var sttModel = sttProvider.ToLowerInvariant() switch
            {
                "nova3"  => "nova-3",
                "scribe" => elevenLabsSettings.ScribeModel,
                _        => "flux-general-multi",
            };
            logger.LogInformation("[STT] call summary provider={Provider} model={Model} callSid={CallSid} avgConfidence={Avg}",
                sttProvider, sttModel, callSid, avgConfidence?.ToString("F3") ?? "n/a");

            await deepgram.DisposeAsync();

            if (avgConfidence.HasValue)
            {
                try
                {
                    var rows = await db.CallSessions
                        .Where(s => s.CallSid == callSid)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.TranscriptionConfidence, avgConfidence.Value));
                    if (rows == 0)
                        logger.LogWarning("Could not find CallSession {CallSid} to save confidence", callSid);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to save transcription confidence for {CallSid}", callSid);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TwilioStreamEndpoint error");
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
                catch { }
            }
        }
    }
}
