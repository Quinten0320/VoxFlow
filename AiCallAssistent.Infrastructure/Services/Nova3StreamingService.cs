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
/// Deepgram Nova-3 implementation of ISttStreamingService.
/// Uses the classic Results/SpeechStarted/UtteranceEnd streaming protocol
/// instead of Flux's TurnInfo protocol — better Dutch accuracy, similar latency.
/// </summary>
public sealed class Nova3StreamingService : ISttStreamingService
{
    private readonly DeepgramSettings _settings;
    private readonly ILogger<Nova3StreamingService> _logger;

    private readonly Channel<string> _transcripts =
        Channel.CreateBounded<string>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly Channel<bool> _speechStarted =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest });

    private ClientWebSocket? _ws;
    private Task? _receiveLoop;
    private CancellationTokenSource? _pendingEotCts;

    // Accumulates is_final partials within one utterance until speech_final fires.
    private readonly StringBuilder _utteranceBuffer = new();

    private double _confidenceSum;
    private int    _confidenceCount;
    public double? AverageConfidence => _confidenceCount > 0 ? _confidenceSum / _confidenceCount : null;

    public ChannelReader<bool> SpeechStartedEvents => _speechStarted.Reader;

    public Nova3StreamingService(IOptions<DeepgramSettings> settings, ILogger<Nova3StreamingService> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task ConnectAsync(string language, CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {_settings.ApiKey}");

        var url = $"{_settings.BaseUrl}/v1/listen"
            + $"?model=nova-3"
            + $"&language=multi"
            + $"&encoding=mulaw"
            + $"&sample_rate=8000"
            + $"&interim_results=true"
            + $"&vad_events=true"
            + $"&utterance_end_ms=800"
            + $"&punctuate=true"
            + $"&smart_format=true";

        _logger.LogInformation("[NOVA3] Connecting to: {Url}", url);
        try
        {
            await _ws.ConnectAsync(new Uri(url), ct);
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "[NOVA3] Handshake failed (errorCode={Code}). URL was: {Url}",
                ex.WebSocketErrorCode, url);
            throw;
        }
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(ct), CancellationToken.None);
        _logger.LogInformation("[NOVA3] Connected successfully");
    }

    public async ValueTask SendAudioAsync(ReadOnlyMemory<byte> mulawBytes, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        await _ws.SendAsync(mulawBytes, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    public async Task CloseAudioAsync(CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        // Finalize tells Nova-3 to flush any pending transcript before closing.
        var finalizeBytes = "{\"type\":\"Finalize\"}"u8.ToArray();
        await _ws.SendAsync(finalizeBytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public IAsyncEnumerable<string> ReadTranscriptsAsync(CancellationToken ct) =>
        _transcripts.Reader.ReadAllAsync(ct);

    public void DrainPendingTranscripts()
    {
        _utteranceBuffer.Clear();
        CancelPendingEot();
        while (_transcripts.Reader.TryRead(out _)) { }
    }

    // Provider/model tags used for the uniform [STT] comparison log line (see PublishTranscript).
    private const string Provider = "nova3";
    private const string Model    = "nova-3";

    /// <summary>
    /// Single funnel for finalised transcripts: writes to the channel and emits one
    /// uniform, greppable log line so Flux / Nova-3 / Scribe can be compared on real calls.
    /// </summary>
    private void PublishTranscript(string text, double? confidence)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        _transcripts.Writer.TryWrite(text);
        _logger.LogInformation("[STT] provider={Provider} model={Model} confidence={Confidence} chars={Chars} transcript=\"{Transcript}\"",
            Provider, Model, confidence?.ToString("F2") ?? "n/a", text.Length, text);
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
            _logger.LogDebug("Deepgram Nova-3 receive loop ended");
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogDebug("Deepgram Nova-3 WebSocket closed prematurely");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deepgram Nova-3 receive loop error");
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
            var type = root?["type"]?.GetValue<string>();

            switch (type)
            {
                case "Metadata":
                    _logger.LogDebug("Nova-3 connected: request_id={Id}", root?["request_id"]?.GetValue<string>());
                    return;

                case "SpeechStarted":
                    // VAD detected speech — cancel grace period and trigger barge-in.
                    CancelPendingEot();
                    _speechStarted.Writer.TryWrite(true);
                    _logger.LogDebug("[NOVA3] SpeechStarted");
                    return;

                case "Results":
                    HandleResults(root!);
                    return;

                case "UtteranceEnd":
                    // Backup flush: if speech_final never fired but silence exceeded utterance_end_ms,
                    // flush whatever is in the buffer.
                    FlushBuffer(source: "UtteranceEnd");
                    return;

                case "Error":
                    _logger.LogWarning("Nova-3 error: code={Code} message={Msg}",
                        root?["error_code"]?.GetValue<string>(),
                        root?["message"]?.GetValue<string>());
                    return;

                default:
                    _logger.LogDebug("Nova-3 unknown message type: {Type}", type);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Nova-3 message");
        }
    }

    private void HandleResults(JsonNode root)
    {
        var isFinal     = root["is_final"]?.GetValue<bool>() ?? false;
        var speechFinal = root["speech_final"]?.GetValue<bool>() ?? false;

        if (!isFinal) return; // Ignore interim partials.

        var transcript  = root["channel"]?["alternatives"]?[0]?["transcript"]?.GetValue<string>() ?? "";
        var confidence  = root["channel"]?["alternatives"]?[0]?["confidence"]?.GetValue<double>();

        if (string.IsNullOrWhiteSpace(transcript)) return;

        // Accumulate this final chunk into the utterance buffer.
        if (_utteranceBuffer.Length > 0) _utteranceBuffer.Append(' ');
        _utteranceBuffer.Append(transcript.Trim());

        if (!speechFinal) return;

        // Speaker finished — flush the full accumulated utterance.
        if (confidence.HasValue)
        {
            _confidenceSum   += confidence.Value;
            _confidenceCount++;
        }

        var full = _utteranceBuffer.ToString().Trim();
        _utteranceBuffer.Clear();
        CancelPendingEot();

        const double GraceThreshold = 0.85;
        const int    GraceMs        = 1200;

        if (confidence.HasValue && confidence.Value < GraceThreshold)
        {
            var lastChar        = full[^1];
            var endsWithTerminal = lastChar is '.' or '?' or '!';

            if (!endsWithTerminal)
            {
                var capturedFull       = full;
                var capturedConfidence = confidence;
                var cts = new CancellationTokenSource();
                _pendingEotCts = cts;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(GraceMs, cts.Token);
                        _logger.LogDebug("[NOVA3] speech_final low-conf grace elapsed");
                        PublishTranscript(capturedFull, capturedConfidence);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("[NOVA3] grace cancelled (user continued): \"{T}\"", capturedFull);
                    }
                }, CancellationToken.None);
                return;
            }
        }

        PublishTranscript(full, confidence);
    }

    private void FlushBuffer(string source)
    {
        var full = _utteranceBuffer.ToString().Trim();
        _utteranceBuffer.Clear();
        CancelPendingEot();

        if (string.IsNullOrEmpty(full)) return;

        _logger.LogDebug("[NOVA3] flush source={Source}", source);
        PublishTranscript(full, null);
    }

    private void CancelPendingEot()
    {
        var cts = Interlocked.Exchange(ref _pendingEotCts, null);
        if (cts == null) return;
        cts.Cancel();
        cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        CancelPendingEot();
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
