using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Services;

namespace AiCallAssistent.Infrastructure.Services;

public sealed class InMemoryConversationStore : IConversationStore, IDisposable
{
    private readonly ConcurrentDictionary<string, ConversationEntry> _store = new();
    private readonly ConcurrentDictionary<string, string> _outcomes = new();
    private readonly Timer _cleanupTimer;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);

    // Storing JSON strings rather than JsonNode keeps memory flat between requests.
    private readonly record struct ConversationEntry(string Json, DateTimeOffset ExpiresAt);

    public InMemoryConversationStore()
    {
        _cleanupTimer = new Timer(RemoveExpiredEntries, null, CleanupInterval, CleanupInterval);
    }

    public JsonArray Load(string conversationId)
    {
        if (_store.TryGetValue(conversationId, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            try { return JsonNode.Parse(entry.Json)?.AsArray() ?? []; }
            catch { /* corrupted entry — start fresh */ }
        }

        return [];
    }

    public void Save(string conversationId, JsonArray contents)
    {
        _store[conversationId] = new ConversationEntry(
            contents.ToJsonString(),
            DateTimeOffset.UtcNow.Add(Ttl));
    }

    public void Initialize(string conversationId, JsonArray initialTurns)
    {
        // TryAdd is idempotent — retried Twilio webhooks won't overwrite an active conversation.
        _store.TryAdd(conversationId, new ConversationEntry(
            initialTurns.ToJsonString(),
            DateTimeOffset.UtcNow.Add(Ttl)));
    }

    public void SetCallOutcome(string conversationId, string callType) =>
        _outcomes[conversationId] = callType;

    public string? GetCallOutcome(string conversationId) =>
        _outcomes.TryGetValue(conversationId, out var t) ? t : null;

    private void RemoveExpiredEntries(object? _)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _store)
        {
            if (entry.ExpiresAt <= now)
                _store.TryRemove(new KeyValuePair<string, ConversationEntry>(key, entry));
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}
