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
    private const int MaxHintsPerGame = 65536;
    private readonly ConcurrentDictionary<string, History> games = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<ulong, Move>> hints = new(StringComparer.Ordinal);
    private readonly int capacity;
    private long clock;

    public PositionMemory(int capacity = 512) => this.capacity = capacity;

    public Move? SuggestedMove(string? gameId, ulong position)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !hints.TryGetValue(gameId, out var moves)) return null;
        return moves.TryGetValue(position, out var move) ? move : null;
    }

    public void RememberMove(string? gameId, ulong position, Move move)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return;
        var moves = hints.GetOrAdd(gameId, _ => new ConcurrentDictionary<ulong, Move>());
        if (moves.Count >= MaxHintsPerGame) moves.Clear();
        moves[position] = move;
    }

    public int CountNext(string? gameId, string nextKey)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !games.TryGetValue(gameId, out var history)) return 0;
        lock (history.Gate) return history.Counts.GetValueOrDefault(nextKey);
    }

    public Dictionary<string, int> Snapshot(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !games.TryGetValue(gameId, out var history))
            return new Dictionary<string, int>(StringComparer.Ordinal);
        lock (history.Gate) return new Dictionary<string, int>(history.Counts, StringComparer.Ordinal);
    }

    public void Commit(string? gameId, Position current, Position next)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return;
        if (!games.ContainsKey(gameId) && games.Count >= MaxGames)
        {
            var oldest = games.ToArray().OrderBy(pair => pair.Value.LastSeen).First();
            games.TryRemove(oldest.Key, out _);
            hints.TryRemove(oldest.Key, out _);
        }
        var history = games.GetOrAdd(gameId, _ => new History());
        lock (history.Gate)
        {
            history.LastSeen = Interlocked.Increment(ref clock);
            if (!history.Counts.ContainsKey(current.Key)) history.Counts[current.Key] = 1;
            history.Counts[next.Key] = history.Counts.GetValueOrDefault(next.Key) + 1;
            if (history.Counts.Count > capacity)
            {
                var oldest = history.Counts.Keys.Take(history.Counts.Count - capacity).ToArray();
                foreach (var key in oldest) history.Counts.Remove(key);
            }
        }
    }

    public void Forget(string gameId)
    {
        games.TryRemove(gameId, out _);
        hints.TryRemove(gameId, out _);
    }

    public int GameCount => games.Count;
}
