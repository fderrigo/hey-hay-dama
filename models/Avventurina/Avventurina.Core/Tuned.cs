using System.Globalization;
using System.Numerics;

namespace Avventurina.Core;

/// <summary>
/// Linear evaluation over a feature vector, with a middle-game and an ending weight per feature interpolated by the
/// number of pieces. Weights are fitted by logistic regression on the outcomes of Avventurina's recorded games
/// (declared as automatic tuning, allowed after the user's instruction of 7 November, 14:10 UTC). Features are
/// computed from White's point of view as White minus Black.
/// </summary>
public static class Tuned
{
    // feature layout
    public const int Men = 0, Kings = 1, Adv = 2 /* 6: rows 1..6 */, FirstKing = 8, Guard = 9, Back = 10, Supported = 11,
        Stuck = 12, Centre = 13, KingCentre = 14, KingCorner = 15, Runaway = 16 /* 5: rows to go 1..5 */, Mobility = 21,
        Blocked = 22, Imbalance = 23, Isolated = 24, Edge = 25, Trade = 26, Tempo = 27, ManSq = 28 /* 32 */,
        KingSq = 60 /* 32 */, Count = 92;

    /// <summary>Weights: [feature] middle game, [Count + feature] ending. Null until loaded.</summary>
    public static double[]? Weights { get; private set; }

    public static void Load(string path)
    {
        var w = File.ReadAllText(path).Split((char[])[',', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (w.Length != 2 * Count) throw new InvalidDataException($"attesi {2 * Count} pesi, trovati {w.Length}");
        Weights = w;
    }

    static readonly uint[] RowMask = Enumerable.Range(0, 8).Select(r => Mask(s => Geometry.Row[s] == r)).ToArray();
    static readonly uint CentreMask = Mask(s => Geometry.Row[s] is 3 or 4 && Geometry.Col[s] is >= 2 and <= 5);
    static readonly uint KingCentreMask = Mask(s => Geometry.Row[s] is >= 2 and <= 5 && Geometry.Col[s] is >= 1 and <= 6);
    static readonly uint CornerMask = Mask(s => s is 0 or 31 or 3 or 28);
    static readonly uint LeftWing = Mask(s => Geometry.Col[s] <= 3), EdgeFiles = Mask(s => Geometry.Col[s] is 0 or 7);
    static readonly uint WhiteAdvanced = Mask(s => Geometry.Row[s] <= 3), BlackAdvanced = Mask(s => Geometry.Row[s] >= 4);
    const uint WhiteGuard = (1u << 29) | (1u << 30), BlackGuard = (1u << 1) | (1u << 2);
    const uint WhiteBack = 0xF0000000u, BlackBack = 0x0000000Fu;
    static readonly uint[] WhiteCone = Cones(true), BlackCone = Cones(false);

    static uint Mask(Func<int, bool> f) { uint m = 0; for (var s = 0; s < 32; s++) if (f(s)) m |= 1u << s; return m; }

    static uint[] Cones(bool white)
    {
        var cones = new uint[32];
        for (var s = 0; s < 32; s++)
        {
            int r = Geometry.Row[s], c = Geometry.Col[s], togo = white ? r : 7 - r;
            for (var k = 1; k <= togo; k++)
                for (var cc = c - k - 1; cc <= c + k + 1; cc++)
                {
                    var t = Geometry.Sq(white ? r - k : r + k, cc);
                    if (t >= 0) cones[s] |= 1u << t;
                }
        }
        return cones;
    }

    /// <summary>Phase 0 (full board) .. 256 (six pieces or fewer).</summary>
    public static int Phase(in Pos p) => Math.Clamp((24 - p.Count) * 256 / 18, 0, 256);

    public static void Features(in Pos p, Span<float> f)
    {
        f.Clear();
        int wm = BitOperations.PopCount(p.WM), wk = BitOperations.PopCount(p.WK);
        int bm = BitOperations.PopCount(p.BM), bk = BitOperations.PopCount(p.BK);
        f[Men] = wm - bm;
        f[Kings] = wk - bk;
        for (var r = 1; r <= 6; r++) f[Adv + r - 1] = BitOperations.PopCount(p.WM & RowMask[7 - r]) - BitOperations.PopCount(p.BM & RowMask[r]);
        f[FirstKing] = (wk > 0 && bk == 0 ? 1 : 0) - (bk > 0 && wk == 0 ? 1 : 0);
        if (bk == 0) { f[Guard] += BitOperations.PopCount(p.WM & WhiteGuard); f[Back] += BitOperations.PopCount(p.WM & WhiteBack); }
        if (wk == 0) { f[Guard] -= BitOperations.PopCount(p.BM & BlackGuard); f[Back] -= BitOperations.PopCount(p.BM & BlackBack); }
        uint white = p.White, black = p.Black, empty = ~p.Occ;
        f[Supported] = BitOperations.PopCount(p.WM & (Eval.UpLeft(white) | Eval.UpRight(white))) - BitOperations.PopCount(p.BM & (Eval.DownLeft(black) | Eval.DownRight(black)));
        f[Stuck] = BitOperations.PopCount(p.WM & ~(Eval.DownRight(empty) | Eval.DownLeft(empty)) & WhiteAdvanced)
                 - BitOperations.PopCount(p.BM & ~(Eval.UpRight(empty) | Eval.UpLeft(empty)) & BlackAdvanced);
        f[Centre] = BitOperations.PopCount(p.WM & CentreMask) - BitOperations.PopCount(p.BM & CentreMask);
        f[KingCentre] = BitOperations.PopCount(p.WK & KingCentreMask) - BitOperations.PopCount(p.BK & KingCentreMask);
        f[KingCorner] = BitOperations.PopCount(p.WK & CornerMask) - BitOperations.PopCount(p.BK & CornerMask);
        Runaways(p.WM, black, p.BK, true, p.Side > 0, f, 1);
        Runaways(p.BM, white, p.WK, false, p.Side < 0, f, -1);
        int wmob = Mob(p, true), bmob = Mob(p, false);
        f[Mobility] = Math.Min(wmob, 20) - Math.Min(bmob, 20);
        f[Blocked] = (wmob == 0 ? -1 : 0) + (bmob == 0 ? 1 : 0);
        f[Imbalance] = Math.Abs(BitOperations.PopCount(p.WM & LeftWing) - BitOperations.PopCount(p.WM & ~LeftWing))
                     - Math.Abs(BitOperations.PopCount(p.BM & LeftWing) - BitOperations.PopCount(p.BM & ~LeftWing));
        var wNear = Eval.UpLeft(white) | Eval.UpRight(white) | Eval.DownLeft(white) | Eval.DownRight(white);
        var bNear = Eval.UpLeft(black) | Eval.UpRight(black) | Eval.DownLeft(black) | Eval.DownRight(black);
        f[Isolated] = BitOperations.PopCount(p.WM & ~wNear) - BitOperations.PopCount(p.BM & ~bNear);
        f[Edge] = BitOperations.PopCount(p.WM & EdgeFiles) - BitOperations.PopCount(p.BM & EdgeFiles);
        var material = (wm - bm) + 1.8f * (wk - bk);
        var total = (wm + bm) + 1.8f * (wk + bk);
        f[Trade] = material * (24 - Math.Min(total, 24)) / 24f;
        f[Tempo] = p.Side;
        // piece-square: White square s against Black on the mirrored square 31 - s
        for (var b = p.WM; b != 0; b &= b - 1) f[ManSq + BitOperations.TrailingZeroCount(b)] += 1;
        for (var b = p.BM; b != 0; b &= b - 1) f[ManSq + 31 - BitOperations.TrailingZeroCount(b)] -= 1;
        for (var b = p.WK; b != 0; b &= b - 1) f[KingSq + BitOperations.TrailingZeroCount(b)] += 1;
        for (var b = p.BK; b != 0; b &= b - 1) f[KingSq + 31 - BitOperations.TrailingZeroCount(b)] -= 1;
    }

    static void Runaways(uint men, uint enemy, uint enemyKings, bool white, bool toMove, Span<float> f, int sign)
    {
        men &= white ? 0x00FFFFFFu : 0xFFFFFF00u;
        for (var b = men; b != 0; b &= b - 1)
        {
            var s = BitOperations.TrailingZeroCount(b);
            int r = Geometry.Row[s], c = Geometry.Col[s], togo = white ? r : 7 - r;
            if (togo < 1 || togo > 5) continue;
            if ((enemy & (white ? WhiteCone[s] : BlackCone[s])) != 0) continue;
            var free = true;
            for (var kb = enemyKings; kb != 0; kb &= kb - 1)
            {
                var ks = BitOperations.TrailingZeroCount(kb);
                var dist = Math.Max(Math.Abs(Geometry.Row[ks] - (white ? 0 : 7)), Math.Abs(Geometry.Col[ks] - c));
                if (dist <= togo + (toMove ? 0 : 1)) { free = false; break; }
            }
            if (free) f[Runaway + togo - 1] += sign;
        }
    }

    static int Mob(in Pos p, bool white)
    {
        var empty = ~p.Occ;
        uint men = white ? p.WM : p.BM, kings = white ? p.WK : p.BK;
        var n = BitOperations.PopCount(Eval.UpLeft(kings) & empty) + BitOperations.PopCount(Eval.UpRight(kings) & empty)
              + BitOperations.PopCount(Eval.DownLeft(kings) & empty) + BitOperations.PopCount(Eval.DownRight(kings) & empty);
        n += white
            ? BitOperations.PopCount(Eval.UpLeft(men) & empty) + BitOperations.PopCount(Eval.UpRight(men) & empty)
            : BitOperations.PopCount(Eval.DownLeft(men) & empty) + BitOperations.PopCount(Eval.DownRight(men) & empty);
        return n;
    }

    /// <summary>Score from the side to move's point of view, in centi-men.</summary>
    public static int Evaluate(in Pos p)
    {
        var w = Weights!;
        Span<float> f = stackalloc float[Count];
        Features(p, f);
        var phase = Phase(p);
        double mid = 0, end = 0;
        for (var i = 0; i < Count; i++)
        {
            if (f[i] == 0) continue;
            mid += w[i] * f[i];
            end += w[Count + i] * f[i];
        }
        var score = (int)Math.Round((mid * (256 - phase) + end * phase) / 256);
        if (p.WM == 0 && p.BM == 0 && Math.Abs(BitOperations.PopCount(p.WK) - BitOperations.PopCount(p.BK)) <= 1
            && Math.Max(BitOperations.PopCount(p.WK), BitOperations.PopCount(p.BK)) <= 2) score /= 4;
        score = Math.Clamp(score, -20000, 20000);
        return p.Side > 0 ? score : -score;
    }
}
