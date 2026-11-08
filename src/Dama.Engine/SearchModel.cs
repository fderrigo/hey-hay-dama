using System.Diagnostics;

namespace Dama.Engine;

/// <summary>Separate reference player. Iterative deepening negamax with alpha-beta and capture extensions.</summary>
public sealed class SearchModel(NeuralModel network)
{
    public Decision Choose(Position position, IReadOnlyList<Move> legal, int maxDepth, int timeMs, CancellationToken cancellation = default)
    {
        var timer = Stopwatch.StartNew();
        long nodes = 0;
        var fallback = network.Choose(position, legal);
        var completed = fallback;
        var path = new HashSet<string>();

        double Search(Position p, int depth, double alpha, double beta, int ply)
        {
            nodes++;
            if ((nodes & 63) == 0 && (timer.ElapsedMilliseconds >= timeMs || cancellation.IsCancellationRequested)) throw new OperationCanceledException();
            var moves = Rules.LegalMoves(p);
            if (moves.Count == 0) return -100_000 + ply;
            if (p.QuietKingPlyCount >= 80 && p.Board.Contains(2) && p.Board.Contains(-2)) return 0;
            // Search-cycle cutoff only. Actual game adjudication uses the fourth repetition.
            var key = p.Key;
            if (path.Contains(key)) return 0;
            if (depth <= 0 && moves[0].Captures.Length == 0) return network.Evaluate(p, p.Turn);
            path.Add(key);
            try
            {
                double best = double.NegativeInfinity;
                foreach (var move in moves.OrderByDescending(m => m.Captures.Length * 100 + (m.Promotes ? 50 : 0)))
                {
                    var score = -Search(Rules.Apply(p, move), depth - 1, -beta, -alpha, ply + 1);
                    best = Math.Max(best, score);
                    alpha = Math.Max(alpha, score);
                    if (alpha >= beta) break;
                }
                return best;
            }
            finally { path.Remove(key); }
        }

        for (var depth = 1; depth <= maxDepth; depth++)
        {
            if (timer.ElapsedMilliseconds >= timeMs || cancellation.IsCancellationRequested) break;
            Move best = completed.Move;
            double bestScore = double.NegativeInfinity;
            try
            {
                foreach (var move in legal.OrderByDescending(m => m.Id == completed.Move.Id))
                {
                    var score = -Search(Rules.Apply(position, move), depth - 1, -double.PositiveInfinity, -bestScore, 1);
                    if (score > bestScore) { bestScore = score; best = move; }
                }
                completed = new Decision(best, bestScore, nodes, depth);
                if (bestScore > 99_000) break;
            }
            catch (OperationCanceledException) { break; }
        }
        return completed with { Nodes = nodes };
    }
}
