using System.Diagnostics;
using System.Numerics;
using Dama.Engine;

namespace Diaspro;

public static class DeepSearch
{
    public static int Choose(Network network, Position position, IReadOnlyList<Move> legal, PositionMemory? memory = null,
        string? gameId = null, int milliseconds = 1000, int repetitionCount = 1, int maximumDepth = 32) =>
        Evaluate(network, position, legal, memory, gameId, milliseconds, repetitionCount, maximumDepth).MoveIndex;

    public static SearchResult Evaluate(Network network, Position position, IReadOnlyList<Move> legal, PositionMemory? memory = null,
        string? gameId = null, int milliseconds = 1000, int repetitionCount = 1, int maximumDepth = 32, bool useEndgameEvaluation = false)
    {
        if (legal.Count == 1) return new SearchResult(0, 0, 1, 0, 0);
        var started = Stopwatch.GetTimestamp();
        var initial = Policy.Values(network, position, legal, memory, gameId);
        var orderValues = (float[])initial.Clone();
        var best = Policy.Choose(initial);
        var context = new Context(network, Stopwatch.GetTimestamp() + (long)(milliseconds / 1000.0 * Stopwatch.Frequency), useEndgameEvaluation,
            memory, gameId);
        var root = BitPosition.FromPosition(position);
        var rootKey = root.Hash();
        var suggested = memory?.SuggestedMove(gameId, rootKey);
        if (suggested is not null)
            for (var index = 0; index < legal.Count; index++)
                if (legal[index].Path.SequenceEqual(suggested.Path))
                {
                    best = index;
                    break;
                }
        var path = PathHistory(memory, gameId, rootKey, repetitionCount, out var historyHash);
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
                    var next = root.Apply(legal[index]);
                    var nextKey = next.Hash();
                    Enter(path, nextKey);
                    float value;
                    try { value = -Negamax(next, depth - 1, float.NegativeInfinity, -alpha, 0, 0, path, historyHash + Hashing.Mix(nextKey), context); }
                    finally { Exit(path, nextKey); }
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
        memory?.RememberMove(gameId, rootKey, legal[best]);
        return new SearchResult(best, selectedValue, completedDepth, context.Nodes, elapsed);
    }

    private static float Negamax(BitPosition position, int depth, float alpha, float beta, int ply, int extension,
        Dictionary<ulong, int> path, ulong historyHash, Context context)
    {
        context.ThrowIfExpired();
        var legal = BitRules.LegalMoves(position);
        var key = position.Hash();
        var repetitions = path.GetValueOrDefault(key);
        if (legal.Count == 0) return -20000 + ply;
        if (repetitions >= 4 || position.QuietKingPlyCount >= 80 && position.WhiteKings != 0 && position.BlackKings != 0) return 0;
        var forced = legal[0].Captures.Length > 0;
        if (depth == 0 && (!forced || extension == 24)) return context.Evaluate(position, legal, key);

        var tableKey = new TranspositionKey(key, historyHash, position.QuietKingPlyCount);
        Move? preferred = context.Memory?.SuggestedMove(context.GameId, key);
        if (context.Table.TryGetValue(tableKey, out var entry))
        {
            preferred = entry.BestMove;
            if (entry.Depth >= depth)
            {
                var cached = FromStorage(entry.Value, ply);
                if (entry.Flag == Bound.Exact || entry.Flag == Bound.Lower && cached >= beta || entry.Flag == Bound.Upper && cached <= alpha)
                    return cached;
            }
        }

        var nextDepth = depth == 0 ? 0 : depth - 1;
        var nextExtension = depth == 0 ? extension + 1 : 0;
        var originalAlpha = alpha;
        var best = float.NegativeInfinity;
        Move? bestMove = null;
        var first = true;
        foreach (var move in Ordered(legal, context, preferred))
        {
            var next = position.Apply(move);
            var nextKey = next.Hash();
            Enter(path, nextKey);
            float value;
            try
            {
                var nextHistory = historyHash + Hashing.Mix(nextKey);
                if (first) value = -Negamax(next, nextDepth, -beta, -alpha, ply + 1, nextExtension, path, nextHistory, context);
                else
                {
                    value = -Negamax(next, nextDepth, -alpha - 1, -alpha, ply + 1, nextExtension, path, nextHistory, context);
                    if (value > alpha && value < beta)
                        value = -Negamax(next, nextDepth, -beta, -alpha, ply + 1, nextExtension, path, nextHistory, context);
                }
            }
            finally { Exit(path, nextKey); }
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
        if (bestMove is not null)
        {
            var flag = best >= beta ? Bound.Lower : best > originalAlpha ? Bound.Exact : Bound.Upper;
            context.Memory?.RememberMove(context.GameId, key, bestMove);
            if (!context.Table.TryGetValue(tableKey, out var previous) || previous.Depth <= depth)
                context.Table[tableKey] = new TranspositionEntry(depth, ToStorage(best, ply), flag, bestMove);
        }
        return best;
    }

    private static Dictionary<ulong, int> PathHistory(PositionMemory? memory, string? gameId, ulong rootKey, int repetitionCount, out ulong hash)
    {
        var path = new Dictionary<ulong, int>();
        if (memory is not null && !string.IsNullOrWhiteSpace(gameId))
            foreach (var pair in memory.Snapshot(gameId)) path[BitPosition.HashKey(pair.Key)] = pair.Value;
        path[rootKey] = Math.Max(repetitionCount, path.GetValueOrDefault(rootKey));
        hash = 0;
        foreach (var pair in path) hash += Hashing.Mix(pair.Key) * (ulong)pair.Value;
        return path;
    }

    private static void Enter(Dictionary<ulong, int> path, ulong key) => path[key] = path.GetValueOrDefault(key) + 1;

    private static void Exit(Dictionary<ulong, int> path, ulong key)
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

    private static float RepetitionPenalty(PositionMemory? memory, string? gameId, BitPosition next, int turn)
    {
        var repetitions = memory?.CountNext(gameId, next.ToPosition().Key) ?? 0;
        var material = turn * (BitOperations.PopCount(next.WhiteMen) + 3 * BitOperations.PopCount(next.WhiteKings)
            - BitOperations.PopCount(next.BlackMen) - 3 * BitOperations.PopCount(next.BlackKings));
        return Math.Min(96, repetitions * (material > 0 ? 24f : material < 0 ? 3f : 12f));
    }

    private static float ToStorage(float value, int ply) => value > 18000 ? value + ply : value < -18000 ? value - ply : value;
    private static float FromStorage(float value, int ply) => value > 18000 ? value - ply : value < -18000 ? value + ply : value;

    private enum Bound : byte { Exact, Lower, Upper }
    private readonly record struct TranspositionKey(ulong Position, ulong History, int QuietKingPlyCount);
    private readonly record struct TranspositionEntry(int Depth, float Value, Bound Flag, Move BestMove);

    private sealed class Context(Network network, long deadline, bool useEndgameEvaluation,
        PositionMemory? memory, string? gameId)
    {
        private readonly Dictionary<(ulong Key, int QuietKingPlyCount), float> evaluations = new();
        public readonly Dictionary<TranspositionKey, TranspositionEntry> Table = new();
        public Network Network { get; } = network;
        public PositionMemory? Memory { get; } = memory;
        public string? GameId { get; } = gameId;
        public bool UseEndgameEvaluation { get; } = useEndgameEvaluation;
        public readonly int[,] History = new int[32, 32];
        public long Nodes => nodes;
        private long nodes;

        public float Evaluate(BitPosition position, IReadOnlyList<Move> legal, ulong key)
        {
            var cacheKey = (key, position.QuietKingPlyCount);
            if (evaluations.TryGetValue(cacheKey, out var cached)) return cached;
            var pieces = BitOperations.PopCount(position.WhiteMen | position.BlackMen | position.WhiteKings | position.BlackKings);
            var value = UseEndgameEvaluation && pieces <= 6
                ? Endgame.Evaluate(position, legal)
                : Network.EvaluateFast(position, legal);
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
