using Dama.Competition;
using Dama.Engine;

namespace Dama.Tests;

public class RulesTests
{
    public static Position Board(int turn, params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return new Position(board, turn);
    }
    private static string Route(Move move) => string.Join('-', move.Path.Select(Rules.Algebraic));

    [Fact] public void OpeningHasCorrectOrientationAndSevenMoves()
    {
        var p = Position.Initial();
        Assert.Equal(1, p.Turn);
        Assert.Equal("A8", Rules.Algebraic(0)); Assert.Equal("H1", Rules.Algebraic(31));
        Assert.Equal(12, p.Board.Count(x => x == 1)); Assert.Equal(12, p.Board.Count(x => x == -1));
        Assert.Equal(["21-17", "21-18", "22-18", "22-19", "23-19", "23-20", "24-20"], Rules.LegalMoves(p).Select(m => m.Id));
    }

    [Fact] public void CaptureCountOutranksKingMover()
    {
        var p = Board(1, ("C4", 1), ("H3", 2), ("D5", -1), ("F7", -1), ("G4", -1));
        var move = Assert.Single(Rules.LegalMoves(p));
        Assert.Equal("C4-E6-G8", Route(move)); Assert.True(move.Promotes);
        var next = Rules.Apply(p, move);
        Assert.Equal(2, next.Board[Rules.ParseSquare("G8")]); Assert.Equal(1, next.Board.Count(x => x == -1));
        Assert.Equal(1, p.Board[Rules.ParseSquare("C4")]);
    }

    [Fact] public void KingMoverWinsEqualCaptureTie()
    {
        var p = Board(1, ("B3", 1), ("F3", 2), ("C4", -1), ("G4", -1));
        Assert.Equal("F3-H5", Route(Assert.Single(Rules.LegalMoves(p))));
    }
    [Fact] public void CapturedKingWinsEqualCountTie()
    {
        var p = Board(1, ("D5", 2), ("C6", -2), ("E6", -1));
        Assert.Equal("D5-B7", Route(Assert.Single(Rules.LegalMoves(p))));
    }
    [Fact] public void FullVictimRankSequenceBreaksTieAndLandingMayRepeat()
    {
        var p = Board(1, ("D7", 2), ("C6", -2), ("C4", -2), ("E4", -2), ("G2", -2), ("G4", -1), ("E2", -1));
        var move = Assert.Single(Rules.LegalMoves(p));
        Assert.Equal("D7-B5-D3-F5-H3-F1-D3", Route(move));
        Assert.Equal([2, 2, 2, 1, 2, 1], move.VictimRanks);
    }
    [Fact] public void PromotionEndsCaptureWithoutBackwardContinuation()
    {
        var p = Board(1, ("C6", 1), ("D7", -1), ("F7", -1));
        Assert.Equal("C6-E8", Route(Assert.Single(Rules.LegalMoves(p))));
    }
    [Fact] public void ManCannotTakeKingOrMoveBackwards()
    {
        var p = Board(1, ("B3", 1), ("C4", -2));
        Assert.Equal("B3-A4", Route(Assert.Single(Rules.LegalMoves(p))));
    }
    [Fact] public void KingsDoNotFlyAndCapturedPiecesBlock()
    {
        var p = Board(1, ("D5", 2), ("C4", -1), ("E6", -1));
        var legal = Rules.LegalMoves(p);
        Assert.Equal(2, legal.Count); // Cannot reverse over a victim just captured.
        Assert.All(legal, m => Assert.Single(m.Captures));
        var kingOnly = Board(1, ("D5", 2));
        Assert.Equal(4, Rules.LegalMoves(kingOnly).Count);
    }
    [Fact] public void BlockedPlayerLosesWithPiecesRemaining()
    {
        var p = Board(1, ("A8", 2), ("B7", -2), ("C6", -1));
        Assert.Equal(new Outcome(-1, "blocked"), Rules.GetOutcome(p));
    }
    [Fact] public void FourfoldAndFortyMovesAreDistinctDraws()
    {
        var p = Board(1, ("A8", 2), ("H1", -2));
        Assert.Null(Rules.GetOutcome(p, 3));
        Assert.Equal(new Outcome(0, "repetition"), Rules.GetOutcome(p, 4));
        Assert.Null(Rules.GetOutcome(p with { QuietKingPlyCount = 79 }));
        Assert.Equal(new Outcome(0, "forty-moves"), Rules.GetOutcome(p with { QuietKingPlyCount = 80 }));
    }
    [Fact] public void ManMoveAndCaptureResetQuietCounter()
    {
        var p = Board(1, ("B3", 1), ("H1", -2)) with { QuietKingPlyCount = 70 };
        Assert.Equal(0, Rules.Apply(p, Rules.LegalMoves(p)[0]).QuietKingPlyCount);
        var k = Board(1, ("D5", 2), ("E6", -1)) with { QuietKingPlyCount = 70 };
        Assert.Equal(0, Rules.Apply(k, Rules.LegalMoves(k)[0]).QuietKingPlyCount);
    }
    [Fact] public void RandomLegalPlayoutsPreserveBoardInvariants()
    {
        var random = new Random(713);
        var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
        for (var game = 0; game < 30; game++)
        {
            var p = Position.Initial();
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            Outcome? outcome = null;
            var player = Participant.Ai("reference-random", "Casuale");
            using var tracked = competition.Start("test", player, player);
            for (var ply = 0; ply < 100; ply++)
            {
                var legal = Rules.LegalMoves(p);
                if (legal.Count == 0) break;
                var move = legal[random.Next(legal.Count)];
                var next = Rules.Apply(p, move);
                Assert.Equal(p.Board.Count(x => x != 0) - move.Captures.Length, next.Board.Count(x => x != 0));
                Assert.Equal(-p.Turn, next.Turn);
                Assert.All(next.Board, piece => Assert.InRange(piece, -2, 2));
                Assert.DoesNotContain(1, next.Board.Take(4)); Assert.DoesNotContain(-1, next.Board.Skip(28));
                p = next;
                seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                outcome = Rules.GetOutcome(p, seen[p.Key]);
                if (outcome is not null) break;
            }
            if (outcome is not null) tracked.Complete(outcome.Winner, p.Ply, outcome.Reason);
            else tracked.Interrupt(p.Ply, "test-ply-limit");
        }
    }
}
