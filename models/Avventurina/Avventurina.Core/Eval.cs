using System.Numerics;

namespace Avventurina.Core;

/// <summary>
/// Hand-written evaluation. Every number below was chosen by reasoning about the game, not fitted to results.
/// Score in centi-men from the side to move's point of view.
/// Lesson carried over from Agata: a man one step from promotion must always be worth less than the king it becomes,
/// so the advancement and runaway terms are capped well below the king value (checked in the self test).
/// </summary>
public static class Eval
{
    public const int Man = 100;
    public const int KingEnding = 200;
    /// <summary>King value with a full board; interpolated towards <see cref="KingEnding"/>.</summary>
    public static int KingOpening { get => kingOpening; set { kingOpening = value; BuildTables(); } }
    static int kingOpening = 170;
    /// <summary>Score per legal move of difference (capped at 20 per side).</summary>
    public static int MobilityWeight { get; set; } = 3;
    /// <summary>Middle-game weights: supported man, stuck advanced man (penalty), centre man.</summary>
    public static int SupportWeight { get; set; } = 3;
    public static int StuckWeight { get; set; } = 8;
    /// <summary>Middle-game penalty for a man with no friendly piece on any of its four diagonal neighbours.</summary>
    public static int IsolatedWeight { get; set; } = 0;
    /// <summary>Middle-game penalty for a man on the a or h file.</summary>
    public static int EdgeWeight { get; set; } = 0;
    /// <summary>Percent applied to the back-rank guard weight (100 = as written).</summary>
    public static int GuardScale { get => guardScale; set { guardScale = value; BuildTables(); } }
    static int guardScale = 100;
    public static int CentreWeight { get => centreWeight; set { centreWeight = value; BuildTables(); } }
    static int centreWeight = 5;
    /// <summary>Middle-game penalty per man of difference between the two wings (columns a-d against e-h).</summary>
    public static int Balance { get; set; } = 10;
    /// <summary>Percent applied to the middle-game advancement table (100 = as written).</summary>
    public static int AdvanceScale
    {
        get => advanceScale;
        set { advanceScale = value; BuildTables(); }
    }
    static int advanceScale = 100;

    // advancement by rows advanced (0 = own back rank, 6 = one step from promotion)
    static readonly int[] AdvanceMiddle = [0, 0, 1, 2, 4, 6, 9];
    static readonly int[] AdvanceEnding = [0, 2, 5, 9, 14, 19, 25];
    // runaway man: free path to promotion that no enemy piece can reach in time; by rows still to go
    static readonly int[] Runaway = [0, 32, 26, 20, 14, 8, 4, 0];

    // white perspective masks (row 0 top). Back rank of White = row 7 (squares 28..31)
    const uint WhiteBack = 0xF0000000u, BlackBack = 0x0000000Fu;
    // the two back squares that keep the opponent out longest in Italian draughts (b8/d8-type guards, here
    // the second and third square of the own back rank)
    const uint WhiteGuard = (1u << 29) | (1u << 30), BlackGuard = (1u << 1) | (1u << 2);
    // centre: rows 3-4, columns 2-5
    static readonly uint Centre = Mask(s => Geometry.Row[s] is 3 or 4 && Geometry.Col[s] is >= 2 and <= 5);
    static readonly uint[] RowMask = Enumerable.Range(0, 8).Select(r => Mask(s => Geometry.Row[s] == r)).ToArray();
    static readonly uint WhiteAdvanced = Mask(s => Geometry.Row[s] <= 3), BlackAdvanced = Mask(s => Geometry.Row[s] >= 4);
    static readonly uint EdgeFiles = Mask(s => Geometry.Col[s] is 0 or 7);
    static readonly uint LeftWing = Mask(s => Geometry.Col[s] <= 3);
    static readonly uint Corner = Mask(s => s is 0 or 31 or 3 or 28);
    static readonly uint KingCentre = Mask(s => Geometry.Row[s] is >= 2 and <= 5 && Geometry.Col[s] is >= 1 and <= 6);

    // cone ahead of a man: for each row between the man and promotion, the columns within reach of the man or of a
    // defender that could step into its path. Any enemy piece inside stops the runaway bonus.
    static readonly uint[] WhiteCone = Cones(true), BlackCone = Cones(false);

    static uint[] Cones(bool white)
    {
        var cones = new uint[32];
        for (var s = 0; s < 32; s++)
        {
            int r = Geometry.Row[s], c = Geometry.Col[s], togo = white ? r : 7 - r;
            for (var k = 1; k <= togo; k++)
            {
                var rr = white ? r - k : r + k;
                for (var cc = c - k - 1; cc <= c + k + 1; cc++)
                {
                    var t = Geometry.Sq(rr, cc);
                    if (t >= 0) cones[s] |= 1u << t;
                }
            }
        }
        return cones;
    }

    static uint Mask(Func<int, bool> f)
    {
        uint m = 0;
        for (var s = 0; s < 32; s++) if (f(s)) m |= 1u << s;
        return m;
    }

    public static int Evaluate(in Pos p)
    {
        if (Tuned.Weights is not null) return Tuned.Evaluate(p);
        var score = White(p);
        return p.Side > 0 ? score : -score;
    }

    // every phase-dependent weight depends only on the number of pieces: precomputed per count (0..32)
    static readonly int[] KingByCount = new int[33], GuardByCount = new int[33], MiddleByCount = new int[33], CentreByCount = new int[33];
    static readonly int[,] AdvanceByCount = new int[33, 8];

    static Eval() => BuildTables();

    static void BuildTables()
    {
        for (var pieces = 0; pieces <= 32; pieces++)
        {
            var phase = Math.Clamp((24 - pieces) * 256 / 18, 0, 256);
            KingByCount[pieces] = (KingOpening * (256 - phase) + KingEnding * phase) / 256;
            GuardByCount[pieces] = ((256 - phase) / 32 + 2) * guardScale / 100;
            MiddleByCount[pieces] = 256 - phase;
            CentreByCount[pieces] = centreWeight * (256 - phase) / 256;
            for (var r = 1; r <= 6; r++) AdvanceByCount[pieces, r] = (AdvanceMiddle[r] * advanceScale / 100 * (256 - phase) + AdvanceEnding[r] * phase) / 256;
        }
    }

    /// <summary>Static score from White's point of view.</summary>
    public static int White(in Pos p)
    {
        int wm = BitOperations.PopCount(p.WM), wk = BitOperations.PopCount(p.WK);
        int bm = BitOperations.PopCount(p.BM), bk = BitOperations.PopCount(p.BK);
        var pieces = wm + wk + bm + bk;
        // phase: 24 pieces = opening, 6 or fewer = ending; weights interpolated per piece count
        var king = KingByCount[pieces];

        var material = (wm - bm) * Man + (wk - bk) * king;
        var score = material;

        // Italian rule: men cannot capture kings. A king facing only men can be opposed by nothing but a king, so
        // the first king is worth more than its material while the opponent has none
        if (wk > 0 && bk == 0) score += 35;
        if (bk > 0 && wk == 0) score -= 35;

        // trading down when ahead: the same material lead counts more with fewer pieces left (idea from Agata v7,
        // here applied to the whole material balance instead of a single channel)
        var total = (wm + bm) * Man + (wk + bk) * king;
        if (total > 0) score += material * (24 * Man - Math.Min(total, 24 * Man)) / (24 * Man) / 3;

        // men: advancement, counted row by row
        for (var r = 1; r <= 6; r++)
        {
            var bonus = AdvanceByCount[pieces, r];
            if (bonus == 0) continue;
            score += bonus * (BitOperations.PopCount(p.WM & RowMask[7 - r]) - BitOperations.PopCount(p.BM & RowMask[r]));
        }

        // back rank: worth a lot while the opponent still has men to crown, little once kings are on the board
        var guardWeight = GuardByCount[pieces];
        if (bk == 0) score += BitOperations.PopCount(p.WM & WhiteGuard) * guardWeight + BitOperations.PopCount(p.WM & WhiteBack) * 2;
        if (wk == 0) score -= BitOperations.PopCount(p.BM & BlackGuard) * guardWeight + BitOperations.PopCount(p.BM & BlackBack) * 2;

        // structure in the middle game: a man with a friendly piece diagonally behind cannot be captured from the
        // front on that diagonal; an advanced man that cannot move is a target and blocks its own side
        var middle = MiddleByCount[pieces];
        var empty = ~p.Occ;
        uint whiteAll = p.White, blackAll = p.Black;
        if (middle > 0)
        {
            var wSupported = BitOperations.PopCount(p.WM & (UpLeft(whiteAll) | UpRight(whiteAll)));
            var bSupported = BitOperations.PopCount(p.BM & (DownLeft(blackAll) | DownRight(blackAll)));
            score += (wSupported - bSupported) * SupportWeight * middle / 256;
            var wStuck = p.WM & ~(DownRight(empty) | DownLeft(empty)) & WhiteAdvanced;
            var bStuck = p.BM & ~(UpRight(empty) | UpLeft(empty)) & BlackAdvanced;
            score -= (BitOperations.PopCount(wStuck) - BitOperations.PopCount(bStuck)) * StuckWeight * middle / 256;
            if (IsolatedWeight != 0)
            {
                var wNear = UpLeft(whiteAll) | UpRight(whiteAll) | DownLeft(whiteAll) | DownRight(whiteAll);
                var bNear = UpLeft(blackAll) | UpRight(blackAll) | DownLeft(blackAll) | DownRight(blackAll);
                score -= (BitOperations.PopCount(p.WM & ~wNear) - BitOperations.PopCount(p.BM & ~bNear)) * IsolatedWeight * middle / 256;
            }
            if (EdgeWeight != 0)
                score -= (BitOperations.PopCount(p.WM & EdgeFiles) - BitOperations.PopCount(p.BM & EdgeFiles)) * EdgeWeight * middle / 256;
            if (Balance != 0)
            {
                var wImb = Math.Abs(BitOperations.PopCount(p.WM & LeftWing) - BitOperations.PopCount(p.WM & ~LeftWing));
                var bImb = Math.Abs(BitOperations.PopCount(p.BM & LeftWing) - BitOperations.PopCount(p.BM & ~LeftWing));
                score -= (wImb - bImb) * Balance * middle / 256;
            }
        }

        // centre men in the middle game
        var centre = CentreByCount[pieces];
        if (centre != 0) score += (BitOperations.PopCount(p.WM & Centre) - BitOperations.PopCount(p.BM & Centre)) * centre;

        // kings: centralised kings dominate, kings in a corner are slow
        if ((p.WK | p.BK) != 0)
        {
            score += (BitOperations.PopCount(p.WK & KingCentre) - BitOperations.PopCount(p.BK & KingCentre)) * 6;
            score -= (BitOperations.PopCount(p.WK & Corner) - BitOperations.PopCount(p.BK & Corner)) * 8;
        }

        // runaways
        score += Runaways(p.WM, p.Black, p.BK, true, p.Side > 0) - Runaways(p.BM, p.White, p.WK, false, p.Side < 0);

        // mobility: a side with few moves gets squeezed (blocked = loss)
        int wmob = Mobility(p, true), bmob = Mobility(p, false);
        score += (wmob - bmob) * MobilityWeight;
        if (wmob == 0) score -= 60;
        if (bmob == 0) score += 60;

        // drawish material: one king against one king, or a single king left against a lone king plus nothing
        if (wm + bm == 0 && Math.Abs(wk - bk) <= 1 && Math.Max(wk, bk) <= 2) score /= 4;

        return score;
    }

    /// <summary>Bonus for men whose promotion square cannot be contested: no enemy piece inside the cone ahead.</summary>
    static int Runaways(uint men, uint enemy, uint enemyKings, bool white, bool toMove)
    {
        var bonus = 0;
        // only men within five rows of promotion can earn the bonus
        men &= white ? 0x00FFFFFFu : 0xFFFFFF00u;
        for (var b = men; b != 0; b &= b - 1)
        {
            var s = BitOperations.TrailingZeroCount(b);
            int r = Geometry.Row[s], c = Geometry.Col[s];
            var togo = white ? r : 7 - r;
            if (togo > 5) continue;
            var free = (enemy & (white ? WhiteCone[s] : BlackCone[s])) == 0;
            if (free && enemyKings != 0)
            {
                // an enemy king closer to the promotion row than the man's tempo can still catch it
                for (var kb = enemyKings; kb != 0; kb &= kb - 1)
                {
                    var ks = BitOperations.TrailingZeroCount(kb);
                    var dist = Math.Max(Math.Abs(Geometry.Row[ks] - (white ? 0 : 7)), Math.Abs(Geometry.Col[ks] - c));
                    if (dist <= togo + (toMove ? 0 : 1)) { free = false; break; }
                }
            }
            if (free) bonus += Runaway[togo];
        }
        return bonus;
    }

    // bit-parallel neighbours. Even rows (0,2,4,6) hold columns 0,2,4,6; odd rows columns 1,3,5,7.
    const uint Even = 0x0F0F0F0Fu, Odd = 0xF0F0F0F0u, Col0 = 0x01010101u, Col7 = 0x80808080u;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static uint UpLeft(uint b) => ((b & Even & ~Col0) >> 5) | ((b & Odd) >> 4);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static uint UpRight(uint b) => ((b & Even) >> 4) | ((b & Odd & ~Col7) >> 3);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static uint DownLeft(uint b) => ((b & Even & ~Col0) << 3) | ((b & Odd) << 4);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static uint DownRight(uint b) => ((b & Even) << 4) | ((b & Odd & ~Col7) << 5);

    static int Mobility(in Pos p, bool white)
    {
        var empty = ~p.Occ;
        uint men = white ? p.WM : p.BM, kings = white ? p.WK : p.BK;
        var n = BitOperations.PopCount(UpLeft(kings) & empty) + BitOperations.PopCount(UpRight(kings) & empty)
              + BitOperations.PopCount(DownLeft(kings) & empty) + BitOperations.PopCount(DownRight(kings) & empty);
        n += white
            ? BitOperations.PopCount(UpLeft(men) & empty) + BitOperations.PopCount(UpRight(men) & empty)
            : BitOperations.PopCount(DownLeft(men) & empty) + BitOperations.PopCount(DownRight(men) & empty);
        return Math.Min(n, 20);
    }
}
