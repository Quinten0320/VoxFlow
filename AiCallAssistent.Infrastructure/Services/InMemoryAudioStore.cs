using System.Collections.Concurrent;
using AiCallAssistent.Application.Services;

namespace AiCallAssistent.Infrastructure.Services;

public sealed class InMemoryAudioStore : IAudioStore, IDisposable
{
    private readonly ConcurrentDictionary<string, AudioEntry> _store = new();
    private readonly Timer _cleanupTimer;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

    private readonly record struct AudioEntry(byte[] Audio, string ContentType, DateTimeOffset ExpiresAt);

    public InMemoryAudioStore()
    {
        _cleanupTimer = new Timer(RemoveExpiredEntries, null, CleanupInterval, CleanupInterval);
    }

    public string Store(byte[] audio, string contentType)
    {
        var id = Guid.NewGuid().ToString("N");
        _store[id] = new AudioEntry(audio, contentType, DateTimeOffset.UtcNow.Add(Ttl));
        return id;
    }

    public (byte[] Audio, string ContentType)? Get(string id)
    {
        if (_store.TryGetValue(id, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
            return (entry.Audio, entry.ContentType);
        return null;
    }

    private void RemoveExpiredEntries(object? _)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _store)
        {
            if (entry.ExpiresAt <= now)
                _store.TryRemove(new KeyValuePair<string, AudioEntry>(key, entry));
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}
