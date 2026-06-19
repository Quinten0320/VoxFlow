using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public sealed class DeepgramStreamingService : IDeepgramStreamingService
{
    private readonly DeepgramSettings _settings;
    private readonly ILogger<DeepgramStreamingService> _logger;

    private readonly Channel<string> _transcripts =
        Channel.CreateBounded<string>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly Channel<bool> _speechStarted =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest });

    private ClientWebSocket? _ws;
    private Task? _receiveLoop;

    private double _confidenceSum;
    private int    _confidenceCount;
    public double? AverageConfidence => _confidenceCount > 0 ? _confidenceSum / _confidenceCount : null;

    public ChannelReader<bool> SpeechStartedEvents => _speechStarted.Reader;

    public DeepgramStreamingService(IOptions<DeepgramSettings> settings, ILogger<DeepgramStreamingService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task ConnectAsync(string language, CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {_settings.ApiKey}");

        // flux-general-multi supports Dutch and 9 other languages via language_hint
        var url = $"{_settings.BaseUrl}/v2/listen"
            + $"?encoding=mulaw&sample_rate=8000"
            + $"&model=flux-general-multi"
            + $"&language_hint={language}"
            + $"&eot_timeout_ms=500"
            + $"&eot_threshold=0.5";

        await _ws.ConnectAsync(new Uri(url), ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(ct), CancellationToken.None);
        _logger.LogDebug("Deepgram Flux WebSocket connected (language_hint={Language})", language);
    }

    public async ValueTask SendAudioAsync(ReadOnlyMemory<byte> mulawBytes, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        // Forward each Twilio 20ms frame (160 bytes) directly to Deepgram without buffering.
        // Keeping latency low here is critical for fast StartOfTurn (barge-in) detection.
        await _ws.SendAsync(mulawBytes, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    public async Task CloseAudioAsync(CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        // Flux requires a JSON CloseStream message to signal end of audio
        var closeBytes = "{\"type\":\"CloseStream\"}"u8.ToArray();
        await _ws.SendAsync(closeBytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public IAsyncEnumerable<string> ReadTranscriptsAsync(CancellationToken ct) =>
        _transcripts.Reader.ReadAllAsync(ct);

    public void DrainPendingTranscripts()
    {
        while (_transcripts.Reader.TryRead(out _)) { }
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
            _logger.LogDebug("Deepgram Flux receive loop ended");
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogDebug("Deepgram Flux WebSocket closed prematurely");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deepgram Flux receive loop error");
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
                case "Connected":
                    _logger.LogDebug("Deepgram Flux connected: request_id={RequestId}",
                        root?["request_id"]?.GetValue<string>());
                    return;

                case "Error":
                    _logger.LogWarning("Deepgram Flux error: code={Code} description={Desc}",
                        root?["code"]?.GetValue<string>(),
                        root?["description"]?.GetValue<string>());
                    return;

                case "TurnInfo":
                    HandleTurnInfo(root!);
                    return;

                default:
                    _logger.LogDebug("Deepgram Flux unknown message type: {Type}", type);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Deepgram Flux message");
        }
    }

    private void HandleTurnInfo(JsonNode root)
    {
        var eventType = root["event"]?.GetValue<string>();
        var transcript = root["transcript"]?.GetValue<string>() ?? "";

        switch (eventType)
        {
            case "StartOfTurn":
                // User started speaking — trigger barge-in if bot is currently talking
                _speechStarted.Writer.TryWrite(true);
                _logger.LogInformation("Deepgram Flux StartOfTurn received");
                break;

            case "EndOfTurn":
                // High-confidence end of turn — transcript is the complete utterance for this turn
                var eotConfidence = root["end_of_turn_confidence"]?.GetValue<double>();
                if (eotConfidence.HasValue)
                {
                    _confidenceSum   += eotConfidence.Value;
                    _confidenceCount++;
                }
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    _transcripts.Writer.TryWrite(transcript.Trim());
                    _logger.LogDebug("Deepgram Flux EndOfTurn (confidence={Confidence}): {Transcript}",
                        eotConfidence, transcript);
                }
                else
                {
                    _logger.LogDebug("Deepgram Flux EndOfTurn with empty transcript — ignoring");
                }
                break;

            case "TurnResumed":
                // User continued speaking after an EagerEndOfTurn — treat as barge-in
                // to interrupt the bot if it started responding speculatively
                _speechStarted.Writer.TryWrite(true);
                _logger.LogInformation("Deepgram Flux TurnResumed received");
                break;

            case "EagerEndOfTurn":
                // Medium-confidence end of turn — ignored for now; we wait for EndOfTurn
                // to avoid processing incomplete utterances
                _logger.LogDebug("Deepgram Flux EagerEndOfTurn (confidence={Confidence}): {Transcript}",
                    root["end_of_turn_confidence"]?.GetValue<double>(), transcript);
                break;

            case "Update":
                // Partial transcript update — ignored
                _logger.LogDebug("Deepgram Flux Update: {Transcript}", transcript);
                break;

            default:
                _logger.LogDebug("Deepgram Flux unknown TurnInfo event: {Event}", eventType);
                break;
        }
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
