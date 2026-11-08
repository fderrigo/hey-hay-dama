using Dama.Engine;

namespace Diaspro.Tests;

public sealed class FastRulesTests
{
    [Fact]
    public void Generator_matches_the_referee_on_random_positions_and_transitions()
    {
        var random = new Random(71237);
        for (var test = 0; test < 1000; test++)
        {
            var board = new int[32];
            for (var count = random.Next(2, 22); count > 0; count--)
                board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            Compare(new Position(board, test % 2 == 0 ? 1 : -1, test, test % 90));
        }
    }

    [Fact]
    public void Generator_matches_the_referee_along_deterministic_walks()
    {
        var random = new Random(991);
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

    private static List<Move> Compare(Position position)
    {
        var expected = Rules.LegalMoves(position);
        var actual = FastRules.LegalMoves(position);
        Assert.Equal(expected.Select(Signature).OrderBy(value => value, StringComparer.Ordinal),
            actual.Select(Signature).OrderBy(value => value, StringComparer.Ordinal));
        foreach (var move in actual)
        {
            var expectedNext = Rules.Apply(position, expected.Single(candidate => candidate.Id == move.Id));
            var actualNext = Rules.Apply(position, move);
            Assert.Equal(expectedNext.Board, actualNext.Board);
            Assert.Equal(expectedNext.Turn, actualNext.Turn);
            Assert.Equal(expectedNext.QuietKingPlyCount, actualNext.QuietKingPlyCount);
        }
        return expected;
    }

    private static string Signature(Move move) => $"{move.Id}|{move.Piece}|{move.Promotes}|{string.Join(',', move.Captures)}|{string.Join(',', move.VictimRanks)}";
}
