using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services;

/// <summary>
/// Per-call orchestrator: bridges Twilio Media Streams WebSocket ↔ Deepgram ↔ Gemini ↔ ElevenLabs.
/// Instantiated directly by TwilioStreamEndpoint, not registered in DI.
/// </summary>
public sealed class CallStreamHandler
{
    private readonly WebSocket _twilioWs;
    private readonly CallSetupData _setup;
    private readonly string _callSid;
    private readonly string _calledNumber;
    private readonly string _callerNumber;
    private readonly IDeepgramStreamingService _deepgram;
    private readonly IGeminiStreamingService _gemini;
    private readonly IElevenLabsStreamingService _elevenlabs;
    private readonly TwilioSettings _twilio;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CallStreamHandler> _logger;

    // ── Pipeline channels ────────────────────────────────────────────────────
    private readonly Channel<byte[]> _audioIn =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    private readonly SemaphoreSlim _wsSendLock = new(1, 1);

    // ── Mutable state ────────────────────────────────────────────────────────
    private string? _streamSid;
    private volatile bool _isBotSpeaking;

    // _suppressBargeIn: true while Gemini is processing (prevents stale StartOfTurn events
    // that arrived during the user's utterance from triggering a premature barge-in).
    // Set to true before RunConversationStreamingAsync, cleared just after _isBotSpeaking=true.
    private volatile bool _suppressBargeIn;

    // Cancelled by MonitorSpeechStartedAsync when a real barge-in fires.
    // Passed (via linked CTS) to ElevenLabs so audio streaming stops immediately.
    private CancellationTokenSource _playbackCts = new();

    // Tracks audio bytes sent to Twilio this turn so we can calculate how long Twilio
    // is still playing after StreamResponseAsync returns (bytes / 8 = milliseconds at 8 kHz mulaw).
    private long _audioBytesThisTurn;
    private DateTimeOffset _botAudioFirstByteSentAt;

    // ── Pre-built call context ───────────────────────────────────────────────
    private readonly CallDispatchContext _dispatchContext;
    private readonly CompanyCallConfig _callConfig;

    public CallStreamHandler(
        WebSocket twilioWebSocket,
        CallSetupData setup,
        string callSid,
        string calledNumber,
        string callerNumber,
        IDeepgramStreamingService deepgram,
        IGeminiStreamingService gemini,
        IElevenLabsStreamingService elevenlabs,
        TwilioSettings twilioSettings,
        IMemoryCache cache,
        ILogger<CallStreamHandler> logger,
        string? streamSid = null)
    {
        _twilioWs        = twilioWebSocket;
        _setup           = setup;
        _callSid         = callSid;
        _calledNumber    = calledNumber;
        _callerNumber    = callerNumber;
        _streamSid       = streamSid;
        _deepgram        = deepgram;
        _gemini          = gemini;
        _elevenlabs      = elevenlabs;
        _twilio  = twilioSettings;
        _cache   = cache;
        _logger  = logger;

        _dispatchContext = new CallDispatchContext(
            setup.CompanyId,
            callerNumber,
            setup.EscalationNumber,
            setup.DepartmentPhones,
            setup.Branch,
            setup.Features);

        _callConfig = new CompanyCallConfig(
            SystemPrompt:         setup.SystemPrompt,
            Language:             setup.Language,
            GreetingMessage:      null,
            AfterHoursMode:       setup.IsAfterHours && setup.AfterHoursMode is { Length: > 0 } ? setup.AfterHoursMode : null,
            AssistantName:        setup.AssistantName,
            Tone:                 setup.Tone,
            AutoTimeGreeting:     setup.AutoTimeGreeting,
            UseCallerName:        setup.UseCallerName,
            TopicsYes:            setup.TopicsYes,
            TopicsNo:             setup.TopicsNo,
            FallbackBehavior:     setup.FallbackBehavior,
            BehaviorInstructions: setup.BehaviorInstructions,
            RoutingRulesJson:     setup.RoutingRulesJson);
    }

    // ── Entry point ──────────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await SendWelcomeAudioAsync(cts.Token);
            await _deepgram.ConnectAsync(_setup.Language, cts.Token);

            var tasks = new[]
            {
                TwilioReceiveLoopAsync(cts.Token),
                DeepgramForwardLoopAsync(cts.Token),
                TranscriptProcessLoopAsync(cts.Token)
            };

            // Cancel remaining tasks when any one completes
            _ = Task.WhenAny(tasks).ContinueWith(_ => cts.Cancel(), TaskScheduler.Default);

            foreach (var task in tasks)
            {
                try { await task; }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
                catch (Exception ex) { _logger.LogError(ex, "Task error for {CallSid}", _callSid); }
            }
        }
        finally
        {
            _wsSendLock.Dispose();
            _playbackCts.Dispose();
        }
    }

    // ── Task A: receive Twilio WS frames ─────────────────────────────────────

    private async Task TwilioReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        var sb = new StringBuilder();

        try
        {
            while (_twilioWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _twilioWs.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) goto done;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                ProcessTwilioMessage(sb.ToString());
            }

            done:;
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _audioIn.Writer.TryComplete();
            _logger.LogDebug("Twilio receive loop ended for {CallSid}", _callSid);
        }
    }

    private void ProcessTwilioMessage(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var evt = root?["event"]?.GetValue<string>();

            switch (evt)
            {
                case "start":
                    _streamSid = root?["streamSid"]?.GetValue<string>();
                    _logger.LogDebug("Twilio stream started — sid={StreamSid} call={CallSid}", _streamSid, _callSid);
                    break;

                case "media":
                    var payload = root?["media"]?["payload"]?.GetValue<string>();
                    if (payload is { Length: > 0 })
                        _audioIn.Writer.TryWrite(Convert.FromBase64String(payload));
                    break;

                case "stop":
                    _logger.LogDebug("Twilio stream stopped for {CallSid}", _callSid);
                    _audioIn.Writer.TryComplete();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Twilio message for {CallSid}", _callSid);
        }
    }

    // ── Task B: forward audio to Deepgram + barge-in monitor ─────────────────

    private async Task DeepgramForwardLoopAsync(CancellationToken ct)
    {
        var speechMonitor = MonitorSpeechStartedAsync(ct);

        try
        {
            await foreach (var chunk in _audioIn.Reader.ReadAllAsync(ct))
                await _deepgram.SendAudioAsync(chunk, ct);

            await _deepgram.CloseAudioAsync(ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _logger.LogDebug("Deepgram forward loop ended for {CallSid}", _callSid);
        }

        await speechMonitor;
    }

    private async Task MonitorSpeechStartedAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _deepgram.SpeechStartedEvents.ReadAllAsync(ct))
            {
                if (_suppressBargeIn || !_isBotSpeaking)
                {
                    _logger.LogInformation("SpeechStarted ignored (suppress={Suppress} speaking={Speaking}) for {CallSid}",
                        _suppressBargeIn, _isBotSpeaking, _callSid);
                    continue;
                }

                _isBotSpeaking = false;
                _logger.LogInformation("[BARGEIN] isBotSpeaking=false, cancelling playbackCts for {CallSid}", _callSid);
                // Cancel the playback CTS so ElevenLabs StreamAsync exits immediately,
                // rather than waiting for the next 20 ms sub-chunk check.
                _playbackCts.Cancel();
                _logger.LogInformation("[BARGEIN] playbackCts cancelled, sending Twilio clear for {CallSid}", _callSid);
                await SendClearToTwilioAsync(ct);
                _logger.LogInformation("[BARGEIN] Barge-in complete (clear sent) for {CallSid}", _callSid);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Task C: process transcripts → Gemini → ElevenLabs → Twilio ──────────

    private async Task TranscriptProcessLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var transcript in _deepgram.ReadTranscriptsAsync(ct))
            {
                _logger.LogDebug("Transcript for {CallSid}: {Transcript}", _callSid, transcript);

                // Suppress barge-in while Gemini processes: StartOfTurn events from the caller's
                // own utterance are still in the channel at this point. MonitorSpeechStartedAsync
                // will consume and discard them (no competing TryRead drain needed).
                _suppressBargeIn = true;
                _logger.LogInformation("[BARGEIN] suppressBargeIn=true (Gemini start) for {CallSid}", _callSid);
                var result = await _gemini.RunConversationStreamingAsync(
                    _dispatchContext, transcript, _callSid, _callConfig, ct);

                if (!result.Success)
                {
                    _suppressBargeIn = false;
                    _logger.LogError("Gemini failed for {CallSid}: {Error}", _callSid, result.Error);
                    await PlayErrorMessageAsync(ct);
                    continue;
                }

                // Prepare a fresh playback CTS for this turn so MonitorSpeechStartedAsync
                // can cancel it the moment barge-in fires, stopping ElevenLabs immediately.
                _playbackCts.Dispose();
                _playbackCts = new CancellationTokenSource();
                _audioBytesThisTurn = 0;
                _logger.LogInformation("[BARGEIN] Fresh playbackCts created for {CallSid}", _callSid);

                // Set _isBotSpeaking BEFORE clearing _suppressBargeIn so MonitorSpeechStartedAsync
                // always sees a consistent pair (volatile ordering guarantee).
                _isBotSpeaking = true;
                _suppressBargeIn = false;
                _logger.LogInformation("[BARGEIN] isBotSpeaking=true, suppressBargeIn=false (playback start) for {CallSid}", _callSid);

                var interrupted = await StreamResponseAsync(result, ct);

                // StreamResponseAsync returns as soon as we've SENT all bytes to Twilio, but
                // Twilio's buffer plays at real-time (8 kHz mulaw = 8 bytes/ms).
                // Keep _isBotSpeaking=true until playback is done so barge-in still works.
                if (!interrupted && !_playbackCts.IsCancellationRequested && _audioBytesThisTurn > 0)
                {
                    var totalPlaybackMs = _audioBytesThisTurn / 8;
                    var elapsedMs = (long)(DateTimeOffset.UtcNow - _botAudioFirstByteSentAt).TotalMilliseconds;
                    var remainingMs = (int)Math.Max(0, totalPlaybackMs - elapsedMs + 200); // +200ms Twilio buffer
                    _logger.LogInformation("[BARGEIN] Playback window: totalMs={Total} elapsedMs={Elapsed} waitingMs={Waiting} for {CallSid}",
                        totalPlaybackMs, elapsedMs, remainingMs, _callSid);

                    if (remainingMs > 0)
                    {
                        try
                        {
                            await Task.Delay(remainingMs, _playbackCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Barge-in fired during the post-send window — send clear to Twilio
                            // (MonitorSpeechStartedAsync already set _isBotSpeaking=false)
                            _logger.LogInformation("[BARGEIN] Barge-in during post-send window for {CallSid}", _callSid);
                            await SendClearToTwilioAsync(ct);
                            interrupted = true;
                        }
                    }
                }

                _isBotSpeaking = false;
                _logger.LogInformation("[BARGEIN] isBotSpeaking=false (playback end, interrupted={Interrupted}) for {CallSid}", interrupted, _callSid);

                if (!interrupted)
                {
                    // Bot finished naturally. If _playbackCts was cancelled right as the last
                    // chunk was sent (late barge-in), keep the user's transcript; otherwise drain.
                    if (!_playbackCts.IsCancellationRequested)
                        _deepgram.DrainPendingTranscripts();
                }

                if (result.AutoTransferNumber is { Length: > 0 } autoTransfer)
                {
                    await InitiateTransferAsync(autoTransfer, null, ct);
                    return;
                }
                if (result.EscalationNumber is { Length: > 0 } escalation)
                {
                    await InitiateTransferAsync(escalation, result.FallbackNumber, ct);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _logger.LogDebug("Transcript process loop ended for {CallSid}", _callSid);
        }
    }

    // ── Response streaming ───────────────────────────────────────────────────

    private async Task<bool> StreamResponseAsync(GeminiStreamResult result, CancellationToken ct)
    {
        var sentenceBuffer = new StringBuilder();
        var interrupted = false;

        try
        {
            await foreach (var token in result.TextStream.WithCancellation(ct))
            {
                if (interrupted) continue; // drain tokens silently so conversation history saves

                sentenceBuffer.Append(token);

                while (TryExtractSentence(sentenceBuffer, out var sentence))
                {
                    interrupted = await StreamSentenceAsync(sentence, ct);
                    if (interrupted) break;
                }
            }

            if (!interrupted && sentenceBuffer.Length > 0)
                await StreamSentenceAsync(sentenceBuffer.ToString(), ct);
        }
        catch (OperationCanceledException) { }

        return interrupted;
    }

    // 20 ms of mulaw audio at 8 kHz = 160 bytes. Sending in these sub-chunks lets us
    // check for barge-in every 20 ms rather than waiting for the next ElevenLabs packet.
    private const int SubChunkBytes = 160;

    private async Task<bool> StreamSentenceAsync(string sentence, CancellationToken ct)
    {
        _logger.LogDebug("Streaming sentence for {CallSid}: {Preview}",
            _callSid, sentence.Length > 60 ? sentence[..60] + "…" : sentence);

        // Link with _playbackCts so MonitorSpeechStartedAsync can abort the ElevenLabs
        // HTTP stream immediately on barge-in rather than waiting for the next sub-chunk.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _playbackCts.Token);
        var playbackCt = linked.Token;

        var elChunks = 0;
        try
        {
            await foreach (var chunk in _elevenlabs.StreamAsync(sentence, playbackCt))
            {
                elChunks++;
                var mem = chunk.AsMemory();
                var offset = 0;

                while (offset < mem.Length)
                {
                    // Belt-and-suspenders check: _isBotSpeaking is set false by
                    // MonitorSpeechStartedAsync before it cancels _playbackCts.
                    if (!_isBotSpeaking)
                    {
                        _logger.LogInformation("[SENTENCE] Exit=barge-in(flag) at chunk {Chunk} byte {Offset}/{Total} for {CallSid}",
                            elChunks, offset, mem.Length, _callSid);
                        return true;
                    }

                    var size = Math.Min(SubChunkBytes, mem.Length - offset);
                    await SendAudioToTwilioAsync(mem.Slice(offset, size), ct);
                    offset += size;
                }
            }
        }
        catch (OperationCanceledException) when (_playbackCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // _playbackCts was cancelled by MonitorSpeechStartedAsync — this is a barge-in.
            _logger.LogInformation("[SENTENCE] Exit=barge-in(CTS) after {Chunks} EL chunks for {CallSid}", elChunks, _callSid);
            return true;
        }

        if (elChunks == 0)
            _logger.LogWarning("ElevenLabs returned 0 audio chunks for {CallSid} — possible API error or silent fail",
                _callSid);
        else
            _logger.LogInformation("[SENTENCE] Exit=natural after {Chunks} EL chunks for {CallSid}", elChunks, _callSid);

        return false;
    }

    private static bool TryExtractSentence(StringBuilder buffer, out string sentence)
    {
        var text = buffer.ToString();

        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] is '.' or '!' or '?' && (char.IsWhiteSpace(text[i + 1]) || text[i + 1] == '\n'))
            {
                sentence = text[..(i + 1)].Trim();
                buffer.Clear();
                var remainder = text[(i + 1)..].TrimStart();
                if (remainder.Length > 0) buffer.Append(remainder);
                return sentence.Length > 0;
            }
        }

        sentence = string.Empty;
        return false;
    }

    // ── Twilio WS send helpers ───────────────────────────────────────────────

    private Task SendAudioToTwilioAsync(ReadOnlyMemory<byte> mulawChunk, CancellationToken ct)
    {
        if (_audioBytesThisTurn == 0)
            _botAudioFirstByteSentAt = DateTimeOffset.UtcNow;
        _audioBytesThisTurn += mulawChunk.Length;
        return SendWsTextAsync(
            $"{{\"event\":\"media\",\"streamSid\":\"{_streamSid}\",\"media\":{{\"payload\":\"{Convert.ToBase64String(mulawChunk.Span)}\"}}}}",
            ct);
    }

    private async Task SendClearToTwilioAsync(CancellationToken ct)
    {
        _logger.LogInformation("[TWILIO] Sending clear event for streamSid={StreamSid} callSid={CallSid}", _streamSid, _callSid);
        await SendWsTextAsync($"{{\"event\":\"clear\",\"streamSid\":\"{_streamSid}\"}}", ct);
        _logger.LogInformation("[TWILIO] Clear event sent for {CallSid}", _callSid);
    }

    private async Task SendWsTextAsync(string json, CancellationToken ct)
    {
        if (_twilioWs.State != WebSocketState.Open) return;

        var bytes = Encoding.UTF8.GetBytes(json);
        await _wsSendLock.WaitAsync(ct);
        try
        {
            await _twilioWs.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _wsSendLock.Release();
        }
    }

    // ── Welcome audio ────────────────────────────────────────────────────────

    private async Task SendWelcomeAudioAsync(CancellationToken ct)
    {
        _isBotSpeaking = true;
        try
        {
            await foreach (var chunk in _elevenlabs.StreamAsync(_setup.WelcomeText, ct))
                await SendAudioToTwilioAsync(chunk.AsMemory(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Welcome audio failed for {CallSid}, continuing without it", _callSid);
        }
        finally
        {
            _isBotSpeaking = false;
        }
    }

    // ── Error message ────────────────────────────────────────────────────────

    private async Task PlayErrorMessageAsync(CancellationToken ct)
    {
        const string errorText = "Er is een fout opgetreden. Probeert u het straks opnieuw.";
        _suppressBargeIn = false;
        _isBotSpeaking = true;
        try
        {
            await foreach (var chunk in _elevenlabs.StreamAsync(errorText, ct))
            {
                if (!_isBotSpeaking) break;
                await SendAudioToTwilioAsync(chunk.AsMemory(), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error message synthesis failed for {CallSid}", _callSid);
        }
        finally
        {
            _isBotSpeaking = false;
        }
    }

    // ── Transfer via <Connect> action URL ────────────────────────────────────
    // Store the target in IMemoryCache, then close the WebSocket.
    // Twilio detects the WebSocket close, calls the <Connect action="..."> URL,
    // and our TwilioController.Transfer() endpoint reads the cache and returns <Dial> TwiML.

    private async Task InitiateTransferAsync(string dialNumber, string? fallbackNumber, CancellationToken ct)
    {
        var cacheValue = fallbackNumber is { Length: > 0 }
            ? $"{dialNumber}|{fallbackNumber}"
            : dialNumber;

        _cache.Set($"transfer_{_callSid}", cacheValue, TimeSpan.FromMinutes(5));
        _logger.LogInformation("Transfer queued for {CallSid} → {Number}", _callSid, dialNumber);

        try
        {
            if (_twilioWs.State == WebSocketState.Open)
                await _twilioWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "transfer", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSocket close threw during transfer for {CallSid}", _callSid);
        }
    }
}
