using Dama.Engine;

namespace Diaspro.Tests;

public sealed class EndgameTests
{
    private static readonly Network Net = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", ManualWeights.Version + ".json"));

    [Fact]
    public void Endgame_evaluation_preserves_colour_symmetry()
    {
        var random = new Random(6107);
        for (var test = 0; test < 250; test++)
        {
            var board = new int[32];
            for (var count = random.Next(2, 7); count > 0; count--)
                board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            var position = new Position(board, test % 2 == 0 ? 1 : -1, QuietKingPlyCount: test % 80);
            var rotated = new Position(board.Reverse().Select(piece => -piece).ToArray(), -position.Turn, QuietKingPlyCount: position.QuietKingPlyCount);
            var first = Endgame.Evaluate(BitPosition.FromPosition(position), BitRules.LegalMoves(BitPosition.FromPosition(position)));
            var second = Endgame.Evaluate(BitPosition.FromPosition(rotated), BitRules.LegalMoves(BitPosition.FromPosition(rotated)));
            Assert.Equal(first, second, 3);
        }
    }

    [Fact]
    public void Endgame_search_still_finds_the_immediate_blockade()
    {
        var position = new Position(Board(("C2", 2), ("A2", -1)));
        var legal = Rules.LegalMoves(position);
        var result = DeepSearch.Evaluate(Net, position, legal, milliseconds: 1000, useEndgameEvaluation: true);
        Assert.Equal(Rules.ParseSquare("B1"), legal[result.MoveIndex].To);
        Assert.Empty(Rules.LegalMoves(Rules.Apply(position, legal[result.MoveIndex])));
    }

    [Fact]
    public void Endgame_values_promotion_progress_and_material()
    {
        var advanced = BitPosition.FromPosition(new Position(Board(("B7", 1), ("H3", -2))));
        var delayed = BitPosition.FromPosition(new Position(Board(("B3", 1), ("H3", -2))));
        Assert.True(Endgame.Evaluate(advanced, BitRules.LegalMoves(advanced)) > Endgame.Evaluate(delayed, BitRules.LegalMoves(delayed)));
        var extraMan = BitPosition.FromPosition(new Position(Board(("B7", 1), ("D3", 1), ("H3", -2))));
        Assert.True(Endgame.Evaluate(extraMan, BitRules.LegalMoves(extraMan)) > Endgame.Evaluate(advanced, BitRules.LegalMoves(advanced)));
    }

    private static int[] Board(params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return board;
    }
}
