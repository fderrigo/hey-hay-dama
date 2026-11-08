using Dama.Engine;

namespace Diaspro.Tests;

public sealed class BitboardTests
{
    private static readonly Network Net = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", ManualWeights.Version + ".json"));

    [Fact]
    public void Bitboard_rules_and_transitions_match_the_referee()
    {
        var random = new Random(71237);
        for (var test = 0; test < 1000; test++)
        {
            var board = new int[32];
            for (var count = random.Next(2, 22); count > 0; count--)
                board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            Compare(new Position(board, test % 2 == 0 ? 1 : -1, test, test % 90));
        }
        for (var walk = 0; walk < 40; walk++)
        {
            var position = Position.Initial();
            for (var ply = 0; ply < 160; ply++)
            {
                var legal = Compare(position);
                if (legal.Count == 0) break;
                position = Rules.Apply(position, legal[random.Next(legal.Count)]);
            }
        }
    }

    [Fact]
    public void Deep_search_matches_v12_at_fixed_depth()
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
            var expected = Search.Evaluate(Net, position, legal, milliseconds: 10000, maximumDepth: 4);
            var actual = DeepSearch.Evaluate(Net, position, legal, milliseconds: 10000, maximumDepth: 4);
            Assert.Equal(4, actual.CompletedDepth);
            Assert.True(Math.Abs(expected.Value - actual.Value) < 0.001f,
                $"v12 {expected.Value}, bitboard {actual.Value}, posizione {position.Key}");
        }
    }

    [Fact]
    public void Deep_search_matches_v12_on_random_positions_and_repetitions()
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
            var expected = Search.Evaluate(Net, position, legal, milliseconds: 10000, maximumDepth: 3);
            var actual = DeepSearch.Evaluate(Net, position, legal, milliseconds: 10000, maximumDepth: 3);
            Assert.True(Math.Abs(expected.Value - actual.Value) < 0.001f,
                $"v12 {expected.Value}, bitboard {actual.Value}, posizione {position.Key}");
            checkedCases++;
        }
        Assert.Equal(20, checkedCases);

        var repeated = new Position(Board(("C2", 2), ("G6", -2), ("A6", -1)));
        var memory = new PositionMemory();
        foreach (var move in Rules.LegalMoves(repeated))
        {
            var next = Rules.Apply(repeated, move);
            for (var repetition = 0; repetition < 3; repetition++) memory.Commit("repetition", repeated, next);
        }
        var legalRepeated = Rules.LegalMoves(repeated);
        var oldResult = Search.Evaluate(Net, repeated, legalRepeated, memory, "repetition", 10000, maximumDepth: 4);
        var newResult = DeepSearch.Evaluate(Net, repeated, legalRepeated, memory, "repetition", 10000, maximumDepth: 4);
        Assert.Equal(oldResult.Value, newResult.Value, 3);
    }

    [Fact]
    public void Deep_search_distinguishes_histories_and_forty_move_boundary()
    {
        var position = new Position(Board(("C2", 2), ("G6", -2), ("A6", -1), ("H3", -1)));
        var legal = Rules.LegalMoves(position);
        for (var history = 0; history < 4; history++)
        {
            var memory = new PositionMemory();
            for (var index = 0; index < legal.Count; index++)
            for (var repetition = 0; repetition < history + index % 2; repetition++)
                memory.Commit($"history-{history}", position, Rules.Apply(position, legal[index]));
            var oldResult = Search.Evaluate(Net, position, legal, memory, $"history-{history}", 10000, maximumDepth: 4);
            var newResult = DeepSearch.Evaluate(Net, position, legal, memory, $"history-{history}", 10000, maximumDepth: 4);
            Assert.Equal(oldResult.Value, newResult.Value, 3);
        }
        foreach (var quietCount in new[] { 78, 79, 80, 81 })
        {
            var boundary = position with { QuietKingPlyCount = quietCount };
            var oldResult = Search.Evaluate(Net, boundary, legal, milliseconds: 10000, maximumDepth: 4);
            var newResult = DeepSearch.Evaluate(Net, boundary, legal, milliseconds: 10000, maximumDepth: 4);
            Assert.Equal(oldResult.Value, newResult.Value, 3);
        }
    }

    [Fact]
    public void Deep_search_reuses_only_move_hints_between_requests()
    {
        var position = new Position(Board(("D3", 1), ("H3", 1), ("D5", -1), ("B7", -1)));
        var legal = Rules.LegalMoves(position);
        var memory = new PositionMemory();
        var first = DeepSearch.Evaluate(Net, position, legal, memory, "hints", 10000, maximumDepth: 4);
        var second = DeepSearch.Evaluate(Net, position, legal, memory, "hints", 10000, maximumDepth: 4);
        Assert.Equal(first.CompletedDepth, second.CompletedDepth);
        Assert.Equal(first.Value, second.Value, 3);
        Assert.Equal(first.MoveIndex, second.MoveIndex);
        Assert.NotNull(memory.SuggestedMove("hints", BitPosition.FromPosition(position).Hash()));
    }

    [Fact]
    public void Pattern_head_is_symmetric_and_zero_initialized()
    {
        var withoutPatterns = new Network(ManualWeights.Build() with { PatternSquare = null, PatternNear = null });
        var zero = new Network(ManualWeights.Build() with
        {
            PatternSquare = Enumerable.Range(0, 4).Select(_ => new float[32]).ToArray(),
            PatternNear = Enumerable.Range(0, 128).Select(_ => new float[16]).ToArray()
        });
        var squares = Enumerable.Range(0, 4).Select(channel => Enumerable.Range(0, 32)
            .Select(square => (channel * 32 + square) * 0.01f).ToArray()).ToArray();
        var near = Enumerable.Range(0, 128).Select(row => Enumerable.Range(0, 16)
            .Select(column => (row * 16 + column) * 0.0001f).ToArray()).ToArray();
        var patterned = new Network(ManualWeights.Build() with { PatternSquare = squares, PatternNear = near });
        Assert.Equal(2309, patterned.ParameterCount);
        var random = new Random(19019);
        for (var test = 0; test < 100; test++)
        {
            var board = new int[32];
            for (var count = random.Next(2, 20); count > 0; count--)
                board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            var position = new Position(board, test % 2 == 0 ? 1 : -1, QuietKingPlyCount: test % 90);
            var rotated = position with { Board = board.Reverse().Select(piece => -piece).ToArray(), Turn = -position.Turn };
            Assert.Equal(withoutPatterns.Evaluate(position), zero.Evaluate(position), 4);
            Assert.True(Math.Abs(patterned.Evaluate(position) - patterned.Evaluate(rotated)) < 0.01f,
                $"Differenza di rotazione con pattern: {Math.Abs(patterned.Evaluate(position) - patterned.Evaluate(rotated))}");
            Assert.Equal(patterned.Evaluate(position), patterned.EvaluateFast(BitPosition.FromPosition(position),
                FastRules.LegalMoves(position)), 4);
        }
    }

    [Fact]
    public void Vector_evaluation_tracks_the_scalar_network()
    {
        var random = new Random(9161);
        for (var test = 0; test < 500; test++)
        {
            var board = new int[32];
            for (var count = random.Next(2, 20); count > 0; count--)
                board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            var position = new Position(board, test % 2 == 0 ? 1 : -1, QuietKingPlyCount: test % 90);
            var legal = FastRules.LegalMoves(position);
            if (legal.Count == 0) continue;
            Assert.Equal(Net.Evaluate(position, legal), Net.EvaluateFast(position, legal), 4);
            Assert.Equal(Net.Evaluate(position, legal), Net.EvaluateFast(BitPosition.FromPosition(position), legal), 4);
        }
    }

    private static List<Move> Compare(Position position)
    {
        var fast = BitPosition.FromPosition(position);
        Assert.Equal(position.Board, fast.ToBoard());
        Assert.Equal(BitPosition.HashKey(position.Key), fast.Hash());
        var expected = Rules.LegalMoves(position);
        var actual = BitRules.LegalMoves(fast);
        var expectedSignatures = expected.Select(Signature).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var actualSignatures = actual.Select(Signature).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.True(expectedSignatures.SequenceEqual(actualSignatures),
            $"Posizione {position.Key}; attese {string.Join(" ", expectedSignatures)}; ottenute {string.Join(" ", actualSignatures)}");
        foreach (var move in actual)
        {
            var expectedNext = Rules.Apply(position, expected.Single(candidate => candidate.Id == move.Id));
            var actualNext = fast.Apply(move).ToPosition();
            Assert.Equal(expectedNext.Board, actualNext.Board);
            Assert.Equal(expectedNext.Turn, actualNext.Turn);
            Assert.Equal(expectedNext.QuietKingPlyCount, actualNext.QuietKingPlyCount);
        }
        return expected;
    }

    private static string Signature(Move move) => $"{move.Id}|{move.Piece}|{move.Promotes}|{string.Join(',', move.Captures)}|{string.Join(',', move.VictimRanks)}";

    private static int[] Board(params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return board;
    }
}
