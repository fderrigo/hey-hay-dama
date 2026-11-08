namespace Avventurina.Core;

/// <summary>
/// Bitboard position for Italian draughts. Square numbering matches Dama.Engine: index 0..31, row = s / 4
/// (row 0 is Black's back rank), column = (row % 2) + 2 * (s % 4). White (+1) moves towards row 0.
/// </summary>
public struct Pos
{
    public uint WM, WK, BM, BK;
    public int Side;   // +1 white to move, -1 black to move
    public int Quiet;  // consecutive non-capturing king moves (arbiter's QuietKingPlyCount)
    public ulong Key;

    public readonly uint White => WM | WK;
    public readonly uint Black => BM | BK;
    public readonly uint Occ => WM | WK | BM | BK;
    public readonly int Count => System.Numerics.BitOperations.PopCount(Occ);

    public static Pos FromBoard(int[] board, int side, int quiet)
    {
        var p = new Pos { Side = side, Quiet = quiet };
        for (var s = 0; s < 32; s++)
            switch (board[s])
            {
                case 1: p.WM |= 1u << s; break;
                case 2: p.WK |= 1u << s; break;
                case -1: p.BM |= 1u << s; break;
                case -2: p.BK |= 1u << s; break;
            }
        p.Key = Zobrist.Compute(p);
        return p;
    }

    public readonly int[] ToBoard()
    {
        var b = new int[32];
        for (var s = 0; s < 32; s++)
        {
            var m = 1u << s;
            b[s] = (WM & m) != 0 ? 1 : (WK & m) != 0 ? 2 : (BM & m) != 0 ? -1 : (BK & m) != 0 ? -2 : 0;
        }
        return b;
    }
}

/// <summary>Search move: effect only (path is irrelevant once from, to and victims are known).</summary>
public struct Mv
{
    public byte From, To;
    public bool Promotes, King;
    public uint Captured;
    public int Priority; // capture priority key, 0 for quiet moves
    public readonly bool IsCapture => Captured != 0;
    public readonly bool Same(in Mv o) => From == o.From && To == o.To && Captured == o.Captured;
}

public static class Geometry
{
    // direction 0: (-1,-1), 1: (-1,+1), 2: (+1,-1), 3: (+1,+1)
    public static readonly int[,] Step = new int[32, 4];
    public static readonly int[,] Jump = new int[32, 4];
    public static readonly int[] Step1 = new int[128], Jump1 = new int[128]; // flat [s * 4 + d]
    public static readonly int[] Row = new int[32];
    public static readonly int[] Col = new int[32];
    public const uint WhitePromotion = 0x0000000Fu; // row 0
    public const uint BlackPromotion = 0xF0000000u; // row 7

    static Geometry()
    {
        int[] dr = [-1, -1, 1, 1], dc = [-1, 1, -1, 1];
        for (var s = 0; s < 32; s++)
        {
            int r = s / 4, c = r % 2 + 2 * (s % 4);
            Row[s] = r; Col[s] = c;
            for (var d = 0; d < 4; d++)
            {
                Step[s, d] = Sq(r + dr[d], c + dc[d]);
                Jump[s, d] = Sq(r + 2 * dr[d], c + 2 * dc[d]);
                Step1[s * 4 + d] = Step[s, d]; Jump1[s * 4 + d] = Jump[s, d];
            }
        }
    }

    public static int Sq(int r, int c) => r is >= 0 and < 8 && c is >= 0 and < 8 && (r + c) % 2 == 0 ? r * 4 + c / 2 : -1;
}

public static class Zobrist
{
    public static readonly ulong[,] Piece = new ulong[4, 32];
    public static readonly ulong BlackToMove;

    static Zobrist()
    {
        var x = 0x9E3779B97F4A7C15ul;
        ulong Next() { x ^= x << 13; x ^= x >> 7; x ^= x << 17; return x; }
        for (var t = 0; t < 4; t++) for (var s = 0; s < 32; s++) Piece[t, s] = Next();
        BlackToMove = Next();
    }

    public static ulong Compute(in Pos p)
    {
        ulong k = p.Side < 0 ? BlackToMove : 0;
        for (var s = 0; s < 32; s++)
        {
            var m = 1u << s;
            if ((p.WM & m) != 0) k ^= Piece[0, s];
            else if ((p.WK & m) != 0) k ^= Piece[1, s];
            else if ((p.BM & m) != 0) k ^= Piece[2, s];
            else if ((p.BK & m) != 0) k ^= Piece[3, s];
        }
        return k;
    }
}

public static class MoveGen
{
    /// <summary>Generates the legal moves (Italian priority rules applied). Returns the count.</summary>
    public static int Generate(in Pos p, Span<Mv> buffer)
    {
        var n = Captures(p, buffer);
        return n > 0 ? n : Quiets(p, buffer);
    }

    public static bool HasCapture(in Pos p)
    {
        // bit-parallel: a jumper, an enemy piece next to it and an empty landing square beyond, per direction.
        // Men capture forward only and never kings; kings capture in all four directions.
        var empty = ~p.Occ;
        if (p.Side > 0)
        {
            uint men = p.WM, kings = p.WK, victims = p.BM, all = p.Black;
            if (((Eval.UpLeft(Eval.UpLeft(men) & victims) | Eval.UpRight(Eval.UpRight(men) & victims)) & empty) != 0) return true;
            if (kings == 0) return false;
            return ((Eval.UpLeft(Eval.UpLeft(kings) & all) | Eval.UpRight(Eval.UpRight(kings) & all)
                   | Eval.DownLeft(Eval.DownLeft(kings) & all) | Eval.DownRight(Eval.DownRight(kings) & all)) & empty) != 0;
        }
        else
        {
            uint men = p.BM, kings = p.BK, victims = p.WM, all = p.White;
            if (((Eval.DownLeft(Eval.DownLeft(men) & victims) | Eval.DownRight(Eval.DownRight(men) & victims)) & empty) != 0) return true;
            if (kings == 0) return false;
            return ((Eval.UpLeft(Eval.UpLeft(kings) & all) | Eval.UpRight(Eval.UpRight(kings) & all)
                   | Eval.DownLeft(Eval.DownLeft(kings) & all) | Eval.DownRight(Eval.DownRight(kings) & all)) & empty) != 0;
        }
    }

    public static int Quiets(in Pos p, Span<Mv> buffer)
    {
        var n = 0;
        var empty = ~p.Occ;
        var white = p.Side > 0;
        uint men = white ? p.WM : p.BM, kings = white ? p.WK : p.BK;
        uint promo = white ? Geometry.WhitePromotion : Geometry.BlackPromotion;
        for (var bits = men; bits != 0; bits &= bits - 1)
        {
            var s = System.Numerics.BitOperations.TrailingZeroCount(bits);
            var b = 1u << s;
            var targets = (white ? Eval.UpLeft(b) | Eval.UpRight(b) : Eval.DownLeft(b) | Eval.DownRight(b)) & empty;
            for (; targets != 0; targets &= targets - 1)
            {
                var t = System.Numerics.BitOperations.TrailingZeroCount(targets);
                buffer[n++] = new Mv { From = (byte)s, To = (byte)t, Promotes = (promo >> t & 1) != 0 };
            }
        }
        for (var bits = kings; bits != 0; bits &= bits - 1)
        {
            var s = System.Numerics.BitOperations.TrailingZeroCount(bits);
            var b = 1u << s;
            var targets = (Eval.UpLeft(b) | Eval.UpRight(b) | Eval.DownLeft(b) | Eval.DownRight(b)) & empty;
            for (; targets != 0; targets &= targets - 1)
                buffer[n++] = new Mv { From = (byte)s, To = (byte)System.Numerics.BitOperations.TrailingZeroCount(targets), King = true };
        }
        return n;
    }

    public static int Captures(in Pos p, Span<Mv> buffer)
    {
        if (!HasCapture(p)) return 0;
        uint own = p.Side > 0 ? p.White : p.Black, enemy = p.Side > 0 ? p.Black : p.White;
        uint enemyKings = p.Side > 0 ? p.BK : p.WK, kings = p.Side > 0 ? p.WK : p.BK;
        var ctx = new Ctx
        {
            Enemy = enemy, EnemyKings = enemyKings, D0 = p.Side > 0 ? 0 : 2,
            Promo = p.Side > 0 ? Geometry.WhitePromotion : Geometry.BlackPromotion
        };
        var n = 0;
        var best = 0;
        for (var bits = own; bits != 0; bits &= bits - 1)
        {
            var s = System.Numerics.BitOperations.TrailingZeroCount(bits);
            ctx.From = s;
            ctx.King = (kings >> s & 1) != 0;
            ctx.Occ = p.Occ & ~(1u << s);
            Dfs(ref ctx, s, 0, 0, 0, 0, buffer, ref n, ref best);
        }
        if (n == 0) return 0;
        // keep only the moves with the highest priority, without duplicates of the same effect
        var m = 0;
        for (var i = 0; i < n; i++)
        {
            if (buffer[i].Priority != best) continue;
            var dup = false;
            for (var j = 0; j < m; j++) if (buffer[j].Same(buffer[i])) { dup = true; break; }
            if (!dup) buffer[m++] = buffer[i];
        }
        return m;
    }

    struct Ctx
    {
        public uint Enemy, EnemyKings, Occ, Promo;
        public int D0, From;
        public bool King;
    }

    static void Dfs(ref Ctx c, int s, uint captured, int count, int kingCount, int rankBits, Span<Mv> buf, ref int n, ref int best)
    {
        var extended = false;
        for (var d = c.King ? 0 : c.D0; d < (c.King ? 4 : c.D0 + 2); d++)
        {
            int over = Geometry.Step1[s * 4 + d], land = Geometry.Jump1[s * 4 + d];
            if (land < 0 || (c.Enemy >> over & 1) == 0 || (captured >> over & 1) != 0 || (c.Occ >> land & 1) != 0) continue;
            var victimKing = (c.EnemyKings >> over & 1) != 0;
            if (!c.King && victimKing) continue;
            extended = true;
            var cap = captured | 1u << over;
            int cnt = count + 1, kc = kingCount + (victimKing ? 1 : 0), rb = rankBits | (victimKing ? 1 << (11 - count) : 0);
            if (!c.King && (c.Promo >> land & 1) != 0)
                Add(ref c, land, cap, cnt, kc, rb, true, buf, ref n, ref best);
            else
                Dfs(ref c, land, cap, cnt, kc, rb, buf, ref n, ref best);
        }
        if (!extended && count > 0) Add(ref c, s, captured, count, kingCount, rankBits, false, buf, ref n, ref best);
    }

    static void Add(ref Ctx c, int to, uint captured, int count, int kingCount, int rankBits, bool promotes, Span<Mv> buf, ref int n, ref int best)
    {
        var priority = count << 17 | (c.King ? 1 << 16 : 0) | kingCount << 12 | rankBits;
        if (priority < best) return;
        if (priority > best) { best = priority; n = 0; }
        if (n >= buf.Length) return;
        buf[n++] = new Mv { From = (byte)c.From, To = (byte)to, Captured = captured, Promotes = promotes, King = c.King, Priority = priority };
    }

    public static Pos Make(in Pos p, in Mv m)
    {
        var q = p;
        uint from = 1u << m.From, to = 1u << m.To;
        var k = p.Key ^ Zobrist.BlackToMove;
        if (p.Side > 0)
        {
            if (m.King) { q.WK = q.WK & ~from | to; k ^= Zobrist.Piece[1, m.From] ^ Zobrist.Piece[1, m.To]; }
            else if (m.Promotes) { q.WM ^= from; q.WK |= to; k ^= Zobrist.Piece[0, m.From] ^ Zobrist.Piece[1, m.To]; }
            else { q.WM = q.WM & ~from | to; k ^= Zobrist.Piece[0, m.From] ^ Zobrist.Piece[0, m.To]; }
            for (var bits = m.Captured; bits != 0; bits &= bits - 1)
            {
                var s = System.Numerics.BitOperations.TrailingZeroCount(bits);
                if ((q.BM >> s & 1) != 0) { q.BM ^= 1u << s; k ^= Zobrist.Piece[2, s]; }
                else { q.BK ^= 1u << s; k ^= Zobrist.Piece[3, s]; }
            }
        }
        else
        {
            if (m.King) { q.BK = q.BK & ~from | to; k ^= Zobrist.Piece[3, m.From] ^ Zobrist.Piece[3, m.To]; }
            else if (m.Promotes) { q.BM ^= from; q.BK |= to; k ^= Zobrist.Piece[2, m.From] ^ Zobrist.Piece[3, m.To]; }
            else { q.BM = q.BM & ~from | to; k ^= Zobrist.Piece[2, m.From] ^ Zobrist.Piece[2, m.To]; }
            for (var bits = m.Captured; bits != 0; bits &= bits - 1)
            {
                var s = System.Numerics.BitOperations.TrailingZeroCount(bits);
                if ((q.WM >> s & 1) != 0) { q.WM ^= 1u << s; k ^= Zobrist.Piece[0, s]; }
                else { q.WK ^= 1u << s; k ^= Zobrist.Piece[1, s]; }
            }
        }
        q.Side = -p.Side;
        q.Quiet = m.King && m.Captured == 0 ? p.Quiet + 1 : 0;
        q.Key = k;
        return q;
    }
}
