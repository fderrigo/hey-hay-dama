using System.Collections.Concurrent;
using Dama.Engine;

namespace Diaspro;

public sealed class PositionMemory
{
    private sealed class History
    {
        public readonly object Gate = new();
        public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
        public long LastSeen;
    }

    private const int MaxGames = 64;
    private readonly ConcurrentDictionary<string, History> games = new(StringComparer.Ordinal);
    private readonly int capacity;
    private long clock;

    public PositionMemory(int capacity = 512) => this.capacity = capacity;

    public int CountNext(string? gameId, string nextKey)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !games.TryGetValue(gameId, out var history)) return 0;
        lock (history.Gate) return history.Counts.GetValueOrDefault(nextKey);
    }

    public void Commit(string? gameId, Position current, Position next)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return;
        if (!games.ContainsKey(gameId) && games.Count >= MaxGames)
        {
            var oldest = games.ToArray().OrderBy(pair => pair.Value.LastSeen).First();
            games.TryRemove(oldest.Key, out _);
        }
        var history = games.GetOrAdd(gameId, _ => new History());
        lock (history.Gate)
        {
            history.LastSeen = Interlocked.Increment(ref clock);
            history.Counts[current.Key] = history.Counts.GetValueOrDefault(current.Key) + 1;
            history.Counts[next.Key] = history.Counts.GetValueOrDefault(next.Key) + 1;
            if (history.Counts.Count > capacity)
            {
                var oldest = history.Counts.Keys.Take(history.Counts.Count - capacity).ToArray();
                foreach (var key in oldest) history.Counts.Remove(key);
            }
        }
    }

    public void Forget(string gameId) => games.TryRemove(gameId, out _);

    public int GameCount => games.Count;
}
