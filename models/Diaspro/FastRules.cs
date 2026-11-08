using Dama.Engine;

namespace Diaspro;

public static class FastRules
{
    private static readonly int[] KingDirections = [-1, 1];
    private static readonly int[] WhiteDirections = [-1];
    private static readonly int[] BlackDirections = [1];

    public static List<Move> LegalMoves(Position position)
    {
        var board = (int[])position.Board.Clone();
        var captures = new List<Move>();
        for (var from = 0; from < 32; from++)
        {
            var piece = board[from];
            if (Math.Sign(piece) != position.Turn) continue;
            Capture(board, from, piece, [from], [], [], 0, captures);
        }
        if (captures.Count > 0)
        {
            var best = captures[0];
            for (var index = 1; index < captures.Count; index++)
                if (ComparePriority(captures[index], best) > 0) best = captures[index];
            var prioritized = new List<Move>();
            foreach (var move in captures)
                if (ComparePriority(move, best) == 0) prioritized.Add(move);
            return prioritized;
        }

        var moves = new List<Move>();
        for (var from = 0; from < 32; from++)
        {
            var piece = board[from];
            if (Math.Sign(piece) != position.Turn) continue;
            var (row, column) = Rules.Coordinates(from);
            foreach (var dr in Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                var to = Rules.Square(row + dr, column + dc);
                if (to >= 0 && board[to] == 0)
                    moves.Add(new Move([from, to], [], piece, [], IsPromotion(piece, row + dr)));
            }
        }
        return moves;
    }

    private static void Capture(int[] board, int from, int piece, List<int> path, List<int> victims,
        List<int> ranks, uint jumped, List<Move> results)
    {
        var extended = false;
        var (row, column) = Rules.Coordinates(from);
        foreach (var dr in Directions(piece))
        foreach (var dc in new[] { -1, 1 })
        {
            var over = Rules.Square(row + dr, column + dc);
            var to = Rules.Square(row + dr * 2, column + dc * 2);
            if (over < 0 || to < 0 || board[to] != 0 || (jumped & (1u << over)) != 0) continue;
            var victim = board[over];
            if (victim == 0 || Math.Sign(victim) == Math.Sign(piece) || Math.Abs(piece) == 1 && Math.Abs(victim) == 2) continue;
            extended = true;
            board[from] = 0;
            board[to] = piece;
            path.Add(to);
            victims.Add(over);
            ranks.Add(Math.Abs(victim));
            var promotes = IsPromotion(piece, row + dr * 2);
            if (promotes) results.Add(new Move(path.ToArray(), victims.ToArray(), piece, ranks.ToArray(), true));
            else Capture(board, to, piece, path, victims, ranks, jumped | (1u << over), results);
            ranks.RemoveAt(ranks.Count - 1);
            victims.RemoveAt(victims.Count - 1);
            path.RemoveAt(path.Count - 1);
            board[to] = 0;
            board[from] = piece;
        }
        if (!extended && victims.Count > 0)
            results.Add(new Move(path.ToArray(), victims.ToArray(), piece, ranks.ToArray(), false));
    }

    private static int ComparePriority(Move a, Move b)
    {
        var count = a.Captures.Length.CompareTo(b.Captures.Length);
        if (count != 0) return count;
        var mover = Math.Abs(a.Piece).CompareTo(Math.Abs(b.Piece));
        if (mover != 0) return mover;
        var kings = 0;
        for (var index = 0; index < a.VictimRanks.Length; index++)
        {
            if (a.VictimRanks[index] == 2) kings++;
            if (b.VictimRanks[index] == 2) kings--;
        }
        if (kings != 0) return kings;
        for (var index = 0; index < a.VictimRanks.Length; index++)
        {
            var rank = a.VictimRanks[index].CompareTo(b.VictimRanks[index]);
            if (rank != 0) return rank;
        }
        return 0;
    }

    private static int[] Directions(int piece) => Math.Abs(piece) == 2
        ? KingDirections : piece > 0 ? WhiteDirections : BlackDirections;

    private static bool IsPromotion(int piece, int row) => piece == 1 && row == 0 || piece == -1 && row == 7;
}
