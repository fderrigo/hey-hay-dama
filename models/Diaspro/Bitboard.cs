using Dama.Engine;

namespace Diaspro;

public readonly record struct BitPosition(uint WhiteMen, uint BlackMen, uint WhiteKings, uint BlackKings, int Turn, int QuietKingPlyCount)
{
    public int PieceAt(int square)
    {
        var bit = 1u << square;
        if ((WhiteMen & bit) != 0) return 1;
        if ((BlackMen & bit) != 0) return -1;
        if ((WhiteKings & bit) != 0) return 2;
        if ((BlackKings & bit) != 0) return -2;
        return 0;
    }

    public int[] ToBoard()
    {
        var board = new int[32];
        for (var square = 0; square < 32; square++) board[square] = PieceAt(square);
        return board;
    }

    public Position ToPosition() => new(ToBoard(), Turn, QuietKingPlyCount: QuietKingPlyCount);

    public BitPosition Rotate() => new(Reverse(BlackMen), Reverse(WhiteMen), Reverse(BlackKings), Reverse(WhiteKings),
        -Turn, QuietKingPlyCount);

    private static uint Reverse(uint value)
    {
        value = (value >> 16) | (value << 16);
        value = ((value & 0xff00ff00u) >> 8) | ((value & 0x00ff00ffu) << 8);
        value = ((value & 0xf0f0f0f0u) >> 4) | ((value & 0x0f0f0f0fu) << 4);
        value = ((value & 0xccccccccu) >> 2) | ((value & 0x33333333u) << 2);
        return ((value & 0xaaaaaaaau) >> 1) | ((value & 0x55555555u) << 1);
    }

    public BitPosition Apply(Move move)
    {
        var whiteMen = WhiteMen;
        var blackMen = BlackMen;
        var whiteKings = WhiteKings;
        var blackKings = BlackKings;
        var from = 1u << move.From;
        var to = 1u << move.To;
        if (move.Piece == 1) whiteMen &= ~from;
        else if (move.Piece == -1) blackMen &= ~from;
        else if (move.Piece == 2) whiteKings &= ~from;
        else blackKings &= ~from;
        foreach (var captured in move.Captures)
        {
            var bit = ~(1u << captured);
            whiteMen &= bit;
            blackMen &= bit;
            whiteKings &= bit;
            blackKings &= bit;
        }
        var piece = move.Promotes ? Math.Sign(move.Piece) * 2 : move.Piece;
        if (piece == 1) whiteMen |= to;
        else if (piece == -1) blackMen |= to;
        else if (piece == 2) whiteKings |= to;
        else blackKings |= to;
        var quiet = Math.Abs(move.Piece) == 2 && move.Captures.Length == 0 ? QuietKingPlyCount + 1 : 0;
        return new BitPosition(whiteMen, blackMen, whiteKings, blackKings, -Turn, quiet);
    }

    public ulong Hash() => Hashing.Mix(WhiteMen + 0x9e3779b97f4a7c15UL)
        ^ Hashing.Mix(BlackMen + 0x632be59bd9b4e019UL)
        ^ Hashing.Mix(WhiteKings + 0x8cb92baa9d3d8c77UL)
        ^ Hashing.Mix(BlackKings + 0x85157af5d65a3b19UL)
        ^ (Turn == 1 ? 0xd6e8feb86659fd93UL : 0);

    public static BitPosition FromPosition(Position position)
    {
        uint whiteMen = 0, blackMen = 0, whiteKings = 0, blackKings = 0;
        for (var square = 0; square < 32; square++)
        {
            var bit = 1u << square;
            switch (position.Board[square])
            {
                case 1: whiteMen |= bit; break;
                case -1: blackMen |= bit; break;
                case 2: whiteKings |= bit; break;
                case -2: blackKings |= bit; break;
            }
        }
        return new BitPosition(whiteMen, blackMen, whiteKings, blackKings, position.Turn, position.QuietKingPlyCount);
    }

    public static ulong HashKey(string key)
    {
        uint whiteMen = 0, blackMen = 0, whiteKings = 0, blackKings = 0;
        for (var square = 0; square < 32; square++)
        {
            var bit = 1u << square;
            switch (key[square] - '2')
            {
                case 1: whiteMen |= bit; break;
                case -1: blackMen |= bit; break;
                case 2: whiteKings |= bit; break;
                case -2: blackKings |= bit; break;
            }
        }
        return new BitPosition(whiteMen, blackMen, whiteKings, blackKings, key[^1] == 'w' ? 1 : -1, 0).Hash();
    }
}

internal static class Hashing
{
    public static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xbf58476d1ce4e5b9UL;
        value ^= value >> 27;
        value *= 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }
}

public static class BitRules
{
    private readonly record struct Step(int To, int Over, int RowDelta);
    private static readonly Step[][] QuietSteps = BuildSteps(1);
    private static readonly Step[][] CaptureSteps = BuildSteps(2);

    public static List<Move> LegalMoves(BitPosition position)
    {
        var board = position.ToBoard();
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
                if (Rules.ComparePriority(captures[index], best) > 0) best = captures[index];
            var prioritized = new List<Move>();
            foreach (var move in captures)
                if (Rules.ComparePriority(move, best) == 0) prioritized.Add(move);
            return prioritized;
        }

        var moves = new List<Move>();
        for (var from = 0; from < 32; from++)
        {
            var piece = board[from];
            if (Math.Sign(piece) != position.Turn) continue;
            foreach (var step in QuietSteps[from])
            {
                if (board[step.To] != 0 || Math.Abs(piece) == 1 && step.RowDelta != -Math.Sign(piece)) continue;
                moves.Add(new Move([from, step.To], [], piece, [], IsPromotion(piece, step.To)));
            }
        }
        return moves;
    }

    private static void Capture(int[] board, int from, int piece, List<int> path, List<int> victims,
        List<int> ranks, uint jumped, List<Move> results)
    {
        var extended = false;
        foreach (var step in CaptureSteps[from])
        {
            var over = step.Over;
            var to = step.To;
            if (board[to] != 0 || (jumped & (1u << over)) != 0) continue;
            var victim = board[over];
            if (victim == 0 || Math.Sign(victim) == Math.Sign(piece) || Math.Abs(piece) == 1 && Math.Abs(victim) == 2) continue;
            if (Math.Abs(piece) == 1 && Math.Sign(step.RowDelta) != -Math.Sign(piece)) continue;
            extended = true;
            board[from] = 0;
            board[to] = piece;
            path.Add(to);
            victims.Add(over);
            ranks.Add(Math.Abs(victim));
            if (IsPromotion(piece, to)) results.Add(new Move(path.ToArray(), victims.ToArray(), piece, ranks.ToArray(), true));
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

    private static Step[][] BuildSteps(int distance)
    {
        var result = new Step[32][];
        for (var square = 0; square < 32; square++)
        {
            var (row, column) = Rules.Coordinates(square);
            var steps = new List<Step>(4);
            foreach (var dr in new[] { -distance, distance })
            foreach (var dc in new[] { -distance, distance })
            {
                var to = Rules.Square(row + dr, column + dc);
                if (to < 0) continue;
                var over = distance == 1 ? -1 : Rules.Square(row + dr / 2, column + dc / 2);
                steps.Add(new Step(to, over, dr));
            }
            result[square] = steps.ToArray();
        }
        return result;
    }

    private static bool IsPromotion(int piece, int square) => piece == 1 && square < 4 || piece == -1 && square >= 28;
}
