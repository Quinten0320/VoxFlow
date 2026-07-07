using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

/// <summary>
/// ElevenLabs Scribe v2 Realtime implementation of <see cref="ISttStreamingService"/>.
/// Selected at startup when <c>Deepgram:SttProvider = "scribe"</c>.
///
/// Chosen over Deepgram for stronger Dutch accuracy and multilingual auto-detection
/// (the model covers ~90 languages, so it satisfies "Dutch-first but must still detect
/// other languages" by leaving the language unset — see <see cref="ElevenLabsSettings.ScribeLanguage"/>).
///
/// Twilio Media Streams deliver G.711 μ-law at 8 kHz; Scribe accepts μ-law natively, so
/// each 20 ms / 160-byte frame is forwarded untouched (base64 in a JSON message) — no
/// transcode, keeping the barge-in path low-latency, mirroring the Deepgram services.
///
/// Turn model mapping onto the interface:
///   • partial_transcript  → interim; the first non-empty partial of a turn is the
///                           barge-in "caller started speaking" signal (SpeechStartedEvents).
///   • committed transcript → the finalised utterance handed to Gemini (ReadTranscriptsAsync).
///
/// NOTE: The official realtime WebSocket reference is behind Cloudflare and could not be
/// fetched at implementation time, so the wire details flagged with "// VERIFY" below
/// (URL path, config-as-query-params, exact message_type / field names) are built from the
/// documented API shape and should be confirmed against the live API with a real key. The
/// receive loop logs and ignores unknown message types, so a wrong guess degrades gracefully
/// rather than crashing the call.
/// </summary>
public sealed class ElevenLabsScribeStreamingService : ISttStreamingService
{
    private readonly ElevenLabsSettings _settings;
    private readonly ILogger<ElevenLabsScribeStreamingService> _logger;

    private readonly Channel<string> _transcripts =
        Channel.CreateBounded<string>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly Channel<bool> _speechStarted =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest });

    private ClientWebSocket? _ws;
    private Task? _receiveLoop;

    // True while we are inside a caller utterance, so we emit exactly one barge-in signal
    // per turn (on the first partial) and reset it when the turn is committed.
    private volatile bool _utteranceActive;

    private double _confidenceSum;
    private int    _confidenceCount;
    public double? AverageConfidence => _confidenceCount > 0 ? _confidenceSum / _confidenceCount : null;

    public ChannelReader<bool> SpeechStartedEvents => _speechStarted.Reader;

    public ElevenLabsScribeStreamingService(
        IOptions<ElevenLabsSettings> settings,
        ILogger<ElevenLabsScribeStreamingService> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task ConnectAsync(string language, CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("xi-api-key", _settings.ApiKey);

        // Language hint: empty => auto-detect (multilingual). We deliberately leave this
        // unset by default so the caller can speak any language and still be transcribed;
        // the multilingual model handles Dutch natively and more accurately than Deepgram.
        var langHint = _settings.ScribeLanguage;

        // VERIFY: exact realtime WS path + whether config is passed as query params or an
        // initial JSON "session config" message. Query params mirror ElevenLabs' TTS
        // stream-input endpoint and avoid guessing a config-message schema.
        var url = $"{_settings.ScribeBaseUrl}/v1/speech-to-text/realtime"
            + $"?model_id={Uri.EscapeDataString(_settings.ScribeModel)}"
            + $"&audio_format=ulaw_8000";
        if (!string.IsNullOrWhiteSpace(langHint))
            url += $"&language_code={Uri.EscapeDataString(langHint)}";

        await _ws.ConnectAsync(new Uri(url), ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(ct), CancellationToken.None);
        _logger.LogDebug("ElevenLabs Scribe WebSocket connected (language={Language}, requested_hint={Requested})",
            string.IsNullOrWhiteSpace(langHint) ? "auto" : langHint, language);
    }

    public async ValueTask SendAudioAsync(ReadOnlyMemory<byte> mulawBytes, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;

        // VERIFY: message_type / field names for the audio input message.
        var msg = new JsonObject
        {
            ["message_type"]  = "input_audio_chunk",
            ["audio_base_64"] = Convert.ToBase64String(mulawBytes.Span),
        };
        await SendJsonAsync(msg, ct);
    }

    public async Task CloseAudioAsync(CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;

        // VERIFY: end-of-audio / flush signal. A commit flushes any buffered audio into a
        // final committed transcript before the socket closes.
        var msg = new JsonObject
        {
            ["message_type"] = "input_audio_chunk",
            ["commit"]       = true,
        };
        await SendJsonAsync(msg, ct);
    }

    public IAsyncEnumerable<string> ReadTranscriptsAsync(CancellationToken ct) =>
        _transcripts.Reader.ReadAllAsync(ct);

    public void DrainPendingTranscripts()
    {
        while (_transcripts.Reader.TryRead(out _)) { }
    }

    private async Task SendJsonAsync(JsonNode msg, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(msg.ToJsonString());
        await _ws!.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();

        try
        {
            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) goto closed;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                ProcessMessage(sb.ToString());
            }

            closed:;
            _logger.LogDebug("ElevenLabs Scribe receive loop ended");
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogDebug("ElevenLabs Scribe WebSocket closed prematurely");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ElevenLabs Scribe receive loop error");
        }
        finally
        {
            _transcripts.Writer.TryComplete();
            _speechStarted.Writer.TryComplete();
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var type = root?["message_type"]?.GetValue<string>();

            switch (type)
            {
                case "partial_transcript":
                    HandlePartial(root!);
                    return;

                // VERIFY: committed/final transcript message_type — handle both likely names.
                case "committed_transcript":
                case "final_transcript":
                case "transcript":
                    HandleCommitted(root!);
                    return;

                // VERIFY: explicit VAD speech-start event, if the API emits one. Barge-in is
                // also derived from the first partial (below), so this is belt-and-suspenders.
                case "speech_started":
                    _utteranceActive = true;
                    _speechStarted.Writer.TryWrite(true);
                    _logger.LogDebug("[SCRIBE] speech_started (VAD)");
                    return;

                case "error":
                    _logger.LogWarning("ElevenLabs Scribe error: {Body}", json);
                    return;

                default:
                    _logger.LogDebug("ElevenLabs Scribe unhandled message_type: {Type}", type);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse ElevenLabs Scribe message");
        }
    }

    private void HandlePartial(JsonNode root)
    {
        var text = root["text"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;

        // First partial of a new turn → caller has started speaking → barge-in signal.
        if (!_utteranceActive)
        {
            _utteranceActive = true;
            _speechStarted.Writer.TryWrite(true);
            _logger.LogDebug("[SCRIBE] first partial → barge-in signal");
        }
    }

    private void HandleCommitted(JsonNode root)
    {
        var text = root["text"]?.GetValue<string>() ?? "";
        var confidence = root["confidence"]?.GetValue<double>();

        // Turn is over regardless of whether it carried text.
        _utteranceActive = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("[SCRIBE] committed transcript empty — ignoring");
            return;
        }

        if (confidence.HasValue)
        {
            _confidenceSum += confidence.Value;
            _confidenceCount++;
        }

        _transcripts.Writer.TryWrite(text.Trim());
        _logger.LogInformation("[SCRIBE] committed confidence={Confidence}: \"{Transcript}\"",
            confidence, text.Trim());
    }

    public async ValueTask DisposeAsync()
    {
        _transcripts.Writer.TryComplete();
        _speechStarted.Writer.TryComplete();

        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch { }
            _ws.Dispose();
        }

        if (_receiveLoop is not null)
            await _receiveLoop.ConfigureAwait(false);
    }
}
