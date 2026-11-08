using Dama.Engine;

namespace Diaspro.Tests;

public sealed class SearchTests
{
    private static readonly Network Net = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", ManualWeights.Version + ".json"));

    [Fact]
    public void Search_finds_the_immediate_blockade()
    {
        var position = new Position(Board(("C2", 2), ("A2", -1)));
        var legal = Rules.LegalMoves(position);
        var chosen = legal[Search.Choose(Net, position, legal, milliseconds: 1000)];
        Assert.Equal(Rules.ParseSquare("B1"), chosen.To);
        Assert.Empty(Rules.LegalMoves(Rules.Apply(position, chosen)));
    }

    [Fact]
    public void Search_returns_a_legal_move_with_a_short_budget()
    {
        var position = Position.Initial();
        var legal = Rules.LegalMoves(position);
        var chosen = Search.Choose(Net, position, legal, milliseconds: 1);
        Assert.Contains(chosen, Enumerable.Range(0, legal.Count));
    }

    [Fact]
    public void Search_keeps_the_only_forced_move()
    {
        var position = new Position(Board(("C2", 1), ("D3", -1), ("F5", -1), ("F7", -1)));
        var legal = Rules.LegalMoves(position);
        Assert.Equal(0, Search.Choose(Net, position, legal, milliseconds: 1));
    }

    [Fact]
    public void Search_matches_exhaustive_minimax_at_fixed_depth()
    {
        var fixtures = new[]
        {
            Position.Initial(),
            new Position(Board(("D3", 1), ("H3", 1), ("D5", -1), ("B7", -1))),
            new Position(Board(("C2", 2), ("G6", -2), ("A6", -1)))
        };
        foreach (var position in fixtures)
        {
            var legal = Rules.LegalMoves(position);
            var result = Search.Evaluate(Net, position, legal, milliseconds: 10000, maximumDepth: 4);
            var path = new Dictionary<string, int> { [position.Key] = 1 };
            var expected = float.NegativeInfinity;
            foreach (var move in legal)
            {
                var next = Rules.Apply(position, move);
                Enter(path, next.Key);
                var value = -Reference(next, 3, 0, 0, path);
                Exit(path, next.Key);
                expected = Math.Max(expected, value);
            }
            Assert.Equal(4, result.CompletedDepth);
            Assert.True(Math.Abs(expected - result.Value) < 0.001f,
                $"Minimax {expected}, alpha-beta {result.Value}, posizione {position.Key}");
        }
    }

    [Fact]
    public void Principal_variation_search_matches_minimax_on_random_positions()
    {
        var random = new Random(4271);
        var checkedCases = 0;
        for (var test = 0; test < 200 && checkedCases < 20; test++)
        {
            var board = new int[32];
            for (var count = random.Next(2, 14); count > 0; count--)
                board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            var position = new Position(board, test % 2 == 0 ? 1 : -1);
            var legal = Rules.LegalMoves(position);
            if (legal.Count < 2) continue;
            var result = Search.Evaluate(Net, position, legal, milliseconds: 10000, maximumDepth: 3);
            var path = new Dictionary<string, int> { [position.Key] = 1 };
            var expected = float.NegativeInfinity;
            foreach (var move in legal)
            {
                var next = Rules.Apply(position, move);
                Enter(path, next.Key);
                expected = Math.Max(expected, -Reference(next, 2, 0, 0, path));
                Exit(path, next.Key);
            }
            Assert.True(Math.Abs(expected - result.Value) < 0.001f,
                $"Minimax {expected}, PVS {result.Value}, posizione {position.Key}");
            checkedCases++;
        }
        Assert.Equal(20, checkedCases);
    }

    [Fact]
    public void Search_counts_the_arrival_at_an_already_repeated_position()
    {
        var position = new Position(Board(("C2", 2), ("G6", -2), ("A6", -1)));
        var legal = Rules.LegalMoves(position);
        var memory = new PositionMemory();
        foreach (var move in legal)
        {
            var next = Rules.Apply(position, move);
            for (var repetition = 0; repetition < 3; repetition++) memory.Commit("repetition", position, next);
        }
        var result = Search.Evaluate(Net, position, legal, memory, "repetition", 10000, maximumDepth: 2);
        var path = memory.Snapshot("repetition");
        path[position.Key] = 1;
        var expected = float.NegativeInfinity;
        foreach (var move in legal)
        {
            var next = Rules.Apply(position, move);
            Enter(path, next.Key);
            Assert.Equal(0, Reference(next, 1, 0, 0, path));
            Exit(path, next.Key);
            var material = Network.Material(next.Board, position.Turn);
            var penalty = Math.Min(96, 3 * (material > 0 ? 24f : material < 0 ? 3f : 12f));
            expected = Math.Max(expected, -penalty);
        }
        Assert.Equal(expected, result.Value);
    }

    private static float Reference(Position position, int depth, int ply, int extension, Dictionary<string, int> path)
    {
        var legal = Rules.LegalMoves(position);
        var outcome = Rules.GetOutcome(position, path.GetValueOrDefault(position.Key), legal);
        if (outcome is not null) return outcome.Winner == 0 ? 0 : -20000 + ply;
        var forced = legal[0].Captures.Length > 0;
        if (depth == 0 && (!forced || extension == 24)) return Net.Evaluate(position);
        var nextDepth = depth == 0 ? 0 : depth - 1;
        var nextExtension = depth == 0 ? extension + 1 : 0;
        var best = float.NegativeInfinity;
        foreach (var move in legal)
        {
            var next = Rules.Apply(position, move);
            Enter(path, next.Key);
            var value = -Reference(next, nextDepth, ply + 1, nextExtension, path);
            Exit(path, next.Key);
            best = Math.Max(best, value);
        }
        return best;
    }

    private static void Enter(Dictionary<string, int> path, string key) => path[key] = path.GetValueOrDefault(key) + 1;

    private static void Exit(Dictionary<string, int> path, string key)
    {
        if (path[key] == 1) path.Remove(key);
        else path[key]--;
    }

    private static int[] Board(params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return board;
    }
}
