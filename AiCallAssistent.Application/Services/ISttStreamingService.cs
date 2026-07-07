using System.Threading.Channels;

namespace AiCallAssistent.Application.Services;

public interface ISttStreamingService : IAsyncDisposable
{
    Task ConnectAsync(string language, CancellationToken ct);
    ValueTask SendAudioAsync(ReadOnlyMemory<byte> mulawBytes, CancellationToken ct);
    Task CloseAudioAsync(CancellationToken ct);
    IAsyncEnumerable<string> ReadTranscriptsAsync(CancellationToken ct);

    /// <summary>
    /// Discards any transcripts already queued in the channel.
    /// Call after the bot finishes speaking naturally (not after a barge-in) so that
    /// impatient "hallo?" follow-ups don't get processed as a second turn.
    /// </summary>
    void DrainPendingTranscripts();

    /// <summary>Fires true whenever Deepgram detects speech starting — used for barge-in.</summary>
    ChannelReader<bool> SpeechStartedEvents { get; }

    /// <summary>Average end_of_turn_confidence across all completed turns. Null if no turns were transcribed.</summary>
    double? AverageConfidence { get; }
}
