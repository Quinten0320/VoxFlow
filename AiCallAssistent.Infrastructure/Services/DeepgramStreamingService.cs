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
    private readonly StringBuilder _transcriptAccumulator = new();

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

        var url = $"{_settings.BaseUrl}/v1/listen"
            + $"?encoding=mulaw&sample_rate=8000"
            + $"&model={_settings.Model}&language={language}"
            + $"&endpointing=300&smart_format=true&interim_results=true";

        await _ws.ConnectAsync(new Uri(url), ct);
        // Receive loop runs independently; use CancellationToken.None so it drains on dispose
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(ct), CancellationToken.None);
        _logger.LogDebug("Deepgram WebSocket connected");
    }

    public async ValueTask SendAudioAsync(ReadOnlyMemory<byte> mulawBytes, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        await _ws.SendAsync(mulawBytes, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    public async Task CloseAudioAsync(CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        // Empty binary frame signals end-of-stream to Deepgram
        await _ws.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Binary, endOfMessage: true, ct);
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
            _logger.LogDebug("Deepgram receive loop ended");
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogDebug("Deepgram WebSocket closed prematurely");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deepgram receive loop error");
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

            if (type == "Results")
            {
                var isFinal = root?["is_final"]?.GetValue<bool>() ?? false;
                if (!isFinal) return;

                var transcript = root?["channel"]?["alternatives"]?[0]?["transcript"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    if (_transcriptAccumulator.Length > 0) _transcriptAccumulator.Append(' ');
                    _transcriptAccumulator.Append(transcript);
                }

                // speech_final=true means the endpointing fired — end of the full utterance.
                // With interim_results=true, is_final fires for every chunk; only speech_final marks the real end.
                var speechFinal = root?["speech_final"]?.GetValue<bool>() ?? false;
                if (!speechFinal) return;

                var fullTranscript = _transcriptAccumulator.ToString().Trim();
                _transcriptAccumulator.Clear();
                if (string.IsNullOrWhiteSpace(fullTranscript)) return;

                _transcripts.Writer.TryWrite(fullTranscript);
                _logger.LogDebug("Deepgram utterance complete: {Transcript}", fullTranscript);
            }
            else if (type == "SpeechStarted")
            {
                _speechStarted.Writer.TryWrite(true);
                _logger.LogDebug("Deepgram speech_started");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Deepgram message");
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
