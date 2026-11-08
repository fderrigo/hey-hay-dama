namespace Dama.Engine;

public sealed record Position(int[] Board, int Turn = 1, int Ply = 0, int QuietKingPlyCount = 0)
{
    public static Position Initial() => new(Enumerable.Range(0, 32).Select(i => i < 12 ? -1 : i >= 20 ? 1 : 0).ToArray());
    public string Key => string.Concat(Board.Select(p => (char)('2' + p))) + (Turn == 1 ? "w" : "b");
}

public sealed record Move(int[] Path, int[] Captures, int Piece, int[] VictimRanks, bool Promotes)
{
    public int From => Path[0];
    public int To => Path[^1];
    public string Id => string.Join('-', Path.Select(i => i + 1));
    public string Notation => string.Join(Captures.Length > 0 ? 'x' : '-', Path.Select(i => i + 1));
}

public static class Rules
{
    public static (int Row, int Column) Coordinates(int square) => (square / 4, (square / 4 % 2) + 2 * (square % 4));
    public static int Square(int row, int column) => row is >= 0 and < 8 && column is >= 0 and < 8 && (row + column) % 2 == 0 ? row * 4 + column / 2 : -1;
    public static string Algebraic(int square)
    {
        var (r, c) = Coordinates(square);
        return $"{(char)('A' + c)}{8 - r}";
    }
    public static int ParseSquare(string name) => Square(8 - (name[1] - '0'), char.ToUpperInvariant(name[0]) - 'A');

    public static List<Move> LegalMoves(Position position)
    {
        var captures = new List<Move>();
        var board = (int[])position.Board.Clone();
        for (var from = 0; from < 32; from++)
        {
            var piece = board[from];
            if (Math.Sign(piece) != position.Turn) continue;
            Capture(board, from, piece, [from], [], [], 0, captures);
        }
        if (captures.Count > 0)
        {
            var best = captures.Aggregate((a, b) => ComparePriority(a, b) >= 0 ? a : b);
            return captures.Where(m => ComparePriority(m, best) == 0).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        }

        var moves = new List<Move>();
        for (var from = 0; from < 32; from++)
        {
            var piece = board[from];
            if (Math.Sign(piece) != position.Turn) continue;
            var (r, c) = Coordinates(from);
            foreach (var dr in Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                var to = Square(r + dr, c + dc);
                if (to >= 0 && board[to] == 0)
                    moves.Add(new Move([from, to], [], piece, [], IsPromotion(piece, r + dr)));
            }
        }
        return moves.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    private static void Capture(int[] board, int from, int piece, List<int> path, List<int> victims, List<int> ranks, uint jumped, List<Move> results)
    {
        var extended = false;
        var (r, c) = Coordinates(from);
        foreach (var dr in Directions(piece))
        foreach (var dc in new[] { -1, 1 })
        {
            var over = Square(r + dr, c + dc);
            var to = Square(r + dr * 2, c + dc * 2);
            if (over < 0 || to < 0 || board[to] != 0 || (jumped & (1u << over)) != 0) continue;
            var victim = board[over];
            if (victim == 0 || Math.Sign(victim) == Math.Sign(piece) || Math.Abs(piece) == 1 && Math.Abs(victim) == 2) continue;
            extended = true;
            board[from] = 0;
            board[to] = piece;
            path.Add(to);
            victims.Add(over);
            ranks.Add(Math.Abs(victim));
            var promotes = IsPromotion(piece, r + dr * 2);
            // Captured pieces remain on the board and block until the entire move ends (FID 5.16).
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

    public static int ComparePriority(Move a, Move b)
    {
        var count = a.Captures.Length.CompareTo(b.Captures.Length);
        if (count != 0) return count;
        var mover = Math.Abs(a.Piece).CompareTo(Math.Abs(b.Piece));
        if (mover != 0) return mover;
        var kings = a.VictimRanks.Count(x => x == 2).CompareTo(b.VictimRanks.Count(x => x == 2));
        if (kings != 0) return kings;
        for (var i = 0; i < a.VictimRanks.Length; i++)
        {
            var rank = a.VictimRanks[i].CompareTo(b.VictimRanks[i]);
            if (rank != 0) return rank;
        }
        return 0;
    }

    public static int[] Directions(int piece) => Math.Abs(piece) == 2 ? [-1, 1] : [-Math.Sign(piece)];
    private static bool IsPromotion(int piece, int row) => piece == 1 && row == 0 || piece == -1 && row == 7;

    // Callers must select the move from LegalMoves. Apply deliberately avoids regenerating the tree.
    public static Position Apply(Position position, Move move)
    {
        var board = (int[])position.Board.Clone();
        board[move.From] = 0;
        foreach (var square in move.Captures) board[square] = 0;
        board[move.To] = move.Promotes ? Math.Sign(move.Piece) * 2 : move.Piece;
        return new Position(board, -position.Turn, position.Ply + 1,
            Math.Abs(move.Piece) == 2 && move.Captures.Length == 0 ? position.QuietKingPlyCount + 1 : 0);
    }

    public static Outcome? GetOutcome(Position position, int repetitions = 1, List<Move>? legal = null)
    {
        legal ??= LegalMoves(position);
        if (legal.Count == 0) return new Outcome(-position.Turn, "blocked");
        if (repetitions >= 4) return new Outcome(0, "repetition");
        if (position.QuietKingPlyCount >= 80 && position.Board.Contains(2) && position.Board.Contains(-2))
            return new Outcome(0, "forty-moves");
        return null;
    }
}

public sealed record Outcome(int Winner, string Reason);
