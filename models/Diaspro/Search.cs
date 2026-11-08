using System.Diagnostics;
using Dama.Engine;

namespace Diaspro;

public static class Search
{
    public static int Choose(Network network, Position position, IReadOnlyList<Move> legal, PositionMemory? memory = null,
        string? gameId = null, int milliseconds = 1000, int repetitionCount = 1, int maximumDepth = 32) =>
        Evaluate(network, position, legal, memory, gameId, milliseconds, repetitionCount, maximumDepth).MoveIndex;

    public static SearchResult Evaluate(Network network, Position position, IReadOnlyList<Move> legal, PositionMemory? memory = null,
        string? gameId = null, int milliseconds = 1000, int repetitionCount = 1, int maximumDepth = 32)
    {
        if (legal.Count == 1) return new SearchResult(0, 0, 1, 0, 0);
        var started = Stopwatch.GetTimestamp();
        var initial = Policy.Values(network, position, legal, memory, gameId);
        var orderValues = (float[])initial.Clone();
        var best = Policy.Choose(initial);
        var context = new Context(network, Stopwatch.GetTimestamp() + (long)(milliseconds / 1000.0 * Stopwatch.Frequency));
        var path = memory?.Snapshot(gameId) ?? new Dictionary<string, int>(StringComparer.Ordinal);
        path[position.Key] = Math.Max(repetitionCount, path.GetValueOrDefault(position.Key));
        var completedDepth = 1;
        var selectedValue = initial[best];
        for (var depth = 2; depth <= maximumDepth; depth++)
        {
            try
            {
                var bestValue = float.NegativeInfinity;
                var depthBest = best;
                var alpha = float.NegativeInfinity;
                foreach (var index in RootOrder(orderValues, best))
                {
                    context.ThrowIfExpired();
                    var next = Rules.Apply(position, legal[index]);
                    Enter(path, next.Key);
                    float value;
                    try { value = -Negamax(next, depth - 1, float.NegativeInfinity, -alpha, 0, 0, path, context); }
                    finally { Exit(path, next.Key); }
                    value -= RepetitionPenalty(memory, gameId, next, position.Turn);
                    orderValues[index] = value;
                    if (value > bestValue)
                    {
                        bestValue = value;
                        depthBest = index;
                    }
                    alpha = Math.Max(alpha, bestValue);
                }
                best = depthBest;
                completedDepth = depth;
                selectedValue = bestValue;
            }
            catch (SearchTimeout) { break; }
        }
        var elapsed = (Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency;
        return new SearchResult(best, selectedValue, completedDepth, context.Nodes, elapsed);
    }

    private static float Negamax(Position position, int depth, float alpha, float beta, int ply, int extension,
        Dictionary<string, int> path, Context context)
    {
        context.ThrowIfExpired();
        var legal = FastRules.LegalMoves(position);
        var key = position.Key;
        var outcome = Rules.GetOutcome(position, path.GetValueOrDefault(key), legal);
        if (outcome is not null) return outcome.Winner == 0 ? 0 : -20000 + ply;
        var forced = legal[0].Captures.Length > 0;
        if (depth == 0 && (!forced || extension == 24)) return context.Evaluate(position, legal, key);
        var nextDepth = depth == 0 ? 0 : depth - 1;
        var nextExtension = depth == 0 ? extension + 1 : 0;
        var preferred = context.PreferredMoves.GetValueOrDefault(key);
        var best = float.NegativeInfinity;
        Move? bestMove = null;
        var first = true;
        foreach (var move in Ordered(legal, context, preferred))
        {
            var next = Rules.Apply(position, move);
            Enter(path, next.Key);
            float value;
            try
            {
                if (first) value = -Negamax(next, nextDepth, -beta, -alpha, ply + 1, nextExtension, path, context);
                else
                {
                    value = -Negamax(next, nextDepth, -alpha - 1, -alpha, ply + 1, nextExtension, path, context);
                    if (value > alpha && value < beta)
                        value = -Negamax(next, nextDepth, -beta, -alpha, ply + 1, nextExtension, path, context);
                }
            }
            finally { Exit(path, next.Key); }
            first = false;
            if (value > best)
            {
                best = value;
                bestMove = move;
            }
            alpha = Math.Max(alpha, value);
            if (alpha >= beta)
            {
                if (move.Captures.Length == 0) context.History[move.From, move.To] += depth * depth;
                break;
            }
        }
        if (bestMove is not null) context.PreferredMoves[key] = bestMove;
        return best;
    }

    private static void Enter(Dictionary<string, int> path, string key) => path[key] = path.GetValueOrDefault(key) + 1;

    private static void Exit(Dictionary<string, int> path, string key)
    {
        if (path[key] == 1) path.Remove(key);
        else path[key]--;
    }

    private static IEnumerable<int> RootOrder(IReadOnlyList<float> values, int best) =>
        Enumerable.Range(0, values.Count).OrderByDescending(index => index == best ? float.PositiveInfinity : values[index]);

    private static IOrderedEnumerable<Move> Ordered(IEnumerable<Move> legal, Context context, Move? preferred) => legal
        .OrderByDescending(move => preferred is not null && move.Path.SequenceEqual(preferred.Path))
        .ThenByDescending(move => move.Captures.Length)
        .ThenByDescending(move => move.VictimRanks.Sum(rank => rank == 2 ? 3 : 1))
        .ThenByDescending(move => context.History[move.From, move.To])
        .ThenByDescending(move => move.Promotes)
        .ThenByDescending(move => Math.Abs(move.Piece) == 1
            ? (move.Piece == 1 ? 7 - Rules.Coordinates(move.To).Row : Rules.Coordinates(move.To).Row) : 0);

    private static float RepetitionPenalty(PositionMemory? memory, string? gameId, Position next, int turn)
    {
        var repetitions = memory?.CountNext(gameId, next.Key) ?? 0;
        var material = Network.Material(next.Board, turn);
        return Math.Min(96, repetitions * (material > 0 ? 24f : material < 0 ? 3f : 12f));
    }

    private sealed class Context(Network network, long deadline)
    {
        private readonly Dictionary<(string Key, int QuietKingPlyCount), float> evaluations = new();
        public readonly Dictionary<string, Move> PreferredMoves = new(StringComparer.Ordinal);
        public Network Network { get; } = network;
        public readonly int[,] History = new int[32, 32];
        public long Nodes => nodes;
        public long EvaluationHits { get; private set; }
        private long nodes;

        public float Evaluate(Position position, IReadOnlyList<Move> legal, string key)
        {
            var cacheKey = (key, position.QuietKingPlyCount);
            if (evaluations.TryGetValue(cacheKey, out var cached))
            {
                EvaluationHits++;
                return cached;
            }
            var value = Network.Evaluate(position, legal);
            evaluations[cacheKey] = value;
            return value;
        }

        public void ThrowIfExpired()
        {
            if ((++nodes & 63) == 0 && Stopwatch.GetTimestamp() >= deadline) throw new SearchTimeout();
        }
    }

    private sealed class SearchTimeout : Exception;
}

public sealed record SearchResult(int MoveIndex, float Value, int CompletedDepth, long Nodes, long ElapsedMilliseconds);
