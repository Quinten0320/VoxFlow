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

    private readonly Channel<bool> _bargeIn =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly SemaphoreSlim _wsSendLock = new(1, 1);

    // ── Mutable state ────────────────────────────────────────────────────────
    private string? _streamSid;
    private volatile bool _isBotSpeaking;

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
                if (!_isBotSpeaking)
                {
                    _logger.LogInformation("SpeechStarted received but bot not speaking — ignoring for {CallSid}", _callSid);
                    continue;
                }

                _isBotSpeaking = false;
                _bargeIn.Writer.TryWrite(true);
                await SendClearToTwilioAsync(ct);
                _logger.LogInformation("Barge-in triggered for {CallSid}", _callSid);
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

                var result = await _gemini.RunConversationStreamingAsync(
                    _dispatchContext, transcript, _callSid, _callConfig, ct);

                if (!result.Success)
                {
                    _logger.LogError("Gemini failed for {CallSid}: {Error}", _callSid, result.Error);
                    await PlayErrorMessageAsync(ct);
                    continue;
                }

                // Drain any stale barge-in that arrived while Gemini was processing (~1-2s).
                // Without this, a SpeechStarted for the caller's own utterance can land in
                // _bargeIn right as we set _isBotSpeaking = true, causing the first audio chunk
                // to trigger an immediate "interrupted" and produce complete silence.
                _bargeIn.Reader.TryRead(out _);
                _isBotSpeaking = true;
                var interrupted = await StreamResponseAsync(result, ct);
                _isBotSpeaking = false;

                if (!interrupted)
                {
                    // Bot finished speaking naturally — discard transcripts that piled up
                    // while we were processing (e.g. impatient "hallo?" from the caller).
                    // Exception: if a barge-in fired right as the last audio chunk was sent
                    // (race condition — bot audio was still in Twilio's buffer when the user spoke),
                    // the barge-in signal is unread in _bargeIn. Consume it and skip the drain
                    // so that the user's transcript is kept.
                    if (!_bargeIn.Reader.TryRead(out _))
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

        var elChunks = 0;
        await foreach (var chunk in _elevenlabs.StreamAsync(sentence, ct))
        {
            elChunks++;
            var mem = chunk.AsMemory();
            var offset = 0;

            while (offset < mem.Length)
            {
                if (_bargeIn.Reader.TryRead(out _))
                {
                    _logger.LogInformation("Barge-in mid-audio for {CallSid} (EL chunk {Chunk}, byte {Offset}/{Total})",
                        _callSid, elChunks, offset, mem.Length);
                    return true;
                }

                var size = Math.Min(SubChunkBytes, mem.Length - offset);
                await SendAudioToTwilioAsync(mem.Slice(offset, size), ct);
                offset += size;
            }
        }

        if (elChunks == 0)
            _logger.LogWarning("ElevenLabs returned 0 audio chunks for {CallSid} — possible API error or silent fail",
                _callSid);

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

    private Task SendAudioToTwilioAsync(ReadOnlyMemory<byte> mulawChunk, CancellationToken ct) =>
        SendWsTextAsync(
            $"{{\"event\":\"media\",\"streamSid\":\"{_streamSid}\",\"media\":{{\"payload\":\"{Convert.ToBase64String(mulawChunk.Span)}\"}}}}",
            ct);

    private Task SendClearToTwilioAsync(CancellationToken ct) =>
        SendWsTextAsync($"{{\"event\":\"clear\",\"streamSid\":\"{_streamSid}\"}}", ct);

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
        _isBotSpeaking = true;
        try
        {
            await foreach (var chunk in _elevenlabs.StreamAsync(errorText, ct))
            {
                if (_bargeIn.Reader.TryRead(out _)) break;
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
