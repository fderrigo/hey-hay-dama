using System.Diagnostics;
using System.Numerics;

namespace Avventurina.Core;

/// <summary>
/// Exact endgame tables computed locally by retrograde analysis from the rules alone (no external data).
/// One table per material signature (white men, white kings, black men, black kings) and side to move.
/// Each entry stores the distance to the end of the game in plies, with the parity giving the result:
/// odd = the side to move wins, even = the side to move loses (0 = no legal move). Byte value = distance + 1;
/// 0 = draw, 255 = impossible placement. Distances follow the rules of the arbiter for captures, priority and
/// promotion. Not modelled: the forty-move rule and repetitions (perfect play does not need repetitions;
/// a win longer than the forty-move rule would allow is reported as a win). Distances are an upper bound
/// that always decreases along the chosen line, which is what the search needs to make progress.
/// </summary>
public sealed class Tablebase
{
    public const int WinScore = 20000;
    public int MaxPieces { get; private set; }
    public string Summary { get; private set; } = "";

    readonly System.Collections.Concurrent.ConcurrentDictionary<int, Table> tables = new();
    static readonly long[,] Binom = new long[33, 8];

    static Tablebase()
    {
        for (var n = 0; n <= 32; n++)
        {
            Binom[n, 0] = 1;
            for (var k = 1; k < 8; k++) Binom[n, k] = n == 0 ? 0 : Binom[n - 1, k - 1] + Binom[n - 1, k];
        }
    }

    sealed unsafe class Table
    {
        public int WM, WK, BM, BK;
        public long NWK, NBM, NBK, Size;
        public byte[]? White, Black;       // while building
        byte* mapped;                       // after building: file mapped read-only, paged in by the OS on demand
        System.IO.MemoryMappedFiles.MemoryMappedFile? file;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor? view;

        public byte Get(int side, long i) => White is not null ? (side > 0 ? White[i] : Black![i]) : mapped[(side > 0 ? 0 : Size) + i];

        public void Map(string path)
        {
            file = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
            view = file.CreateViewAccessor(0, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
            byte* ptr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            mapped = ptr + view.PointerOffset;
            White = Black = null;
        }
    }

    static int Sig(int wm, int wk, int bm, int bk) => wm | wk << 4 | bm << 8 | bk << 12;

    public static Tablebase LoadOrBuild(string? directory, int maxPieces = 4, Action<string>? log = null)
    {
        directory ??= Path.Combine(Path.GetTempPath(), "avventurina-tb");
        var tb = new Tablebase();
        var clock = Stopwatch.StartNew();
        Directory.CreateDirectory(directory);
        long positions = 0;
        for (var pieces = 2; pieces <= maxPieces; pieces++)
        {
            var sigs = new List<(int, int, int, int)>();
            for (var w = 1; w < pieces; w++)
            {
                var b = pieces - w;
                for (var wm = 0; wm <= w; wm++)
                    for (var bm = 0; bm <= b; bm++)
                        sigs.Add((wm, w - wm, bm, b - bm));
            }
            // groups with the same number of men are independent (captures go to fewer pieces, promotions to fewer
            // men), so each group is built in parallel after the previous ones
            foreach (var group in sigs.GroupBy(s => s.Item1 + s.Item3).OrderBy(g => g.Key))
            {
                var built = new System.Collections.Concurrent.ConcurrentBag<long>();
                Parallel.ForEach(group, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
                    sig => built.Add(tb.Build(sig.Item1, sig.Item2, sig.Item3, sig.Item4, directory).Size * 2));
                positions += built.Sum();
            }
            tb.MaxPieces = pieces;
            log?.Invoke($"  tabelle fino a {pieces} pezzi pronte, {clock.Elapsed.TotalSeconds:F0} s");
        }
        tb.Summary = $"{tb.tables.Count} tabelle, {positions} posizioni, {clock.Elapsed.TotalSeconds:F1} s";
        log?.Invoke(tb.Summary);
        return tb;
    }

    Table Build(int wm, int wk, int bm, int bk, string directory)
    {
        var t = new Table
        {
            WM = wm, WK = wk, BM = bm, BK = bk,
            NWK = Binom[32, wk], NBM = Binom[28, bm], NBK = Binom[32, bk]
        };
        t.Size = Binom[28, wm] * t.NWK * t.NBM * t.NBK;
        var file = Path.Combine(directory, $"{wm}{wk}{bm}{bk}.tb");
        if (File.Exists(file) && new FileInfo(file).Length == t.Size * 2)
        {
            t.Map(file);
            tables[Sig(wm, wk, bm, bk)] = t;
            return t;
        }
        t.White = new byte[t.Size];
        t.Black = new byte[t.Size];
        tables[Sig(wm, wk, bm, bk)] = t; // children inside the same table are read during the passes
        var buffer = new Mv[256];
        // initial pass: impossible placements and positions without moves
        for (long i = 0; i < t.Size; i++)
        {
            if (!Decode(t, i, out var p)) { t.White[i] = 255; t.Black[i] = 255; continue; }
            for (var s = 0; s < 2; s++)
            {
                p.Side = s == 0 ? 1 : -1;
                if (MoveGen.Generate(p, buffer) == 0) (s == 0 ? t.White : t.Black)[i] = 1;
            }
        }
        // iterative passes over the unresolved entries until nothing changes
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var s = 0; s < 2; s++)
            {
                var arr = s == 0 ? t.White : t.Black;
                for (long i = 0; i < t.Size; i++)
                {
                    if (arr[i] != 0) continue;
                    Decode(t, i, out var p);
                    p.Side = s == 0 ? 1 : -1;
                    var n = MoveGen.Generate(p, buffer);
                    int bestWin = int.MaxValue, worstLoss = -1;
                    var allWin = true;
                    for (var k = 0; k < n; k++)
                    {
                        var c = MoveGen.Make(p, buffer[k]);
                        var v = Lookup(c);
                        if (v == 0) { allWin = false; continue; }       // draw or not yet known
                        var d = v - 1;
                        if (d % 2 == 0) { allWin = false; bestWin = Math.Min(bestWin, d + 1); } // child mover loses
                        else worstLoss = Math.Max(worstLoss, d + 1);
                    }
                    if (bestWin != int.MaxValue) { arr[i] = (byte)Math.Min(bestWin + 1, 252); changed = true; }
                    else if (allWin && worstLoss >= 0) { arr[i] = (byte)Math.Min(worstLoss + 1, 253); changed = true; }
                }
            }
        }
        var tmp = file + ".tmp";
        using (var f = File.Create(tmp))
        {
            f.Write(t.White);
            f.Write(t.Black);
        }
        File.Move(tmp, file, true);
        t.Map(file);
        return t;
    }

    /// <summary>Raw byte for a position (0 draw/unknown, distance+1 otherwise). Positions without a side's pieces count as lost for that side.</summary>
    byte Lookup(in Pos p)
    {
        uint own = p.Side > 0 ? p.White : p.Black, enemy = p.Side > 0 ? p.Black : p.White;
        if (own == 0) return 1;
        if (enemy == 0) return 0; // cannot arise after a legal move
        if (!tables.TryGetValue(Sig(BitOperations.PopCount(p.WM), BitOperations.PopCount(p.WK), BitOperations.PopCount(p.BM), BitOperations.PopCount(p.BK)), out var t))
            return 0;
        var i = Index(t, p);
        return t.Get(p.Side, i);
    }

    public bool Probe(in Pos p, out int score)
    {
        score = 0;
        uint own = p.Side > 0 ? p.White : p.Black, enemy = p.Side > 0 ? p.Black : p.White;
        if (own == 0 || enemy == 0) return false;
        if (!tables.TryGetValue(Sig(BitOperations.PopCount(p.WM), BitOperations.PopCount(p.WK), BitOperations.PopCount(p.BM), BitOperations.PopCount(p.BK)), out var t))
            return false;
        var i = Index(t, p);
        var v = t.Get(p.Side, i);
        if (v == 255) return false;
        if (v == 0) return true;
        var d = v - 1;
        score = d % 2 == 1 ? WinScore - d : -(WinScore - d);
        return true;
    }

    static long Rank(uint mask, int offset)
    {
        long r = 0;
        var k = 1;
        for (var b = mask; b != 0; b &= b - 1, k++) r += Binom[BitOperations.TrailingZeroCount(b) - offset, k];
        return r;
    }

    static long Index(Table t, in Pos p) =>
        ((Rank(p.WM, 4) * t.NWK + Rank(p.WK, 0)) * t.NBM + Rank(p.BM, 0)) * t.NBK + Rank(p.BK, 0);

    static uint Unrank(long r, int k, int offset, int domain)
    {
        uint mask = 0;
        for (var j = k; j >= 1; j--)
        {
            var x = j - 1;
            while (x + 1 < domain && Binom[x + 1, j] <= r) x++;
            r -= Binom[x, j];
            mask |= 1u << (x + offset);
        }
        return mask;
    }

    static bool Decode(Table t, long i, out Pos p)
    {
        var ibk = i % t.NBK; i /= t.NBK;
        var ibm = i % t.NBM; i /= t.NBM;
        var iwk = i % t.NWK; i /= t.NWK;
        p = new Pos
        {
            WM = Unrank(i, t.WM, 4, 28), WK = Unrank(iwk, t.WK, 0, 32),
            BM = Unrank(ibm, t.BM, 0, 28), BK = Unrank(ibk, t.BK, 0, 32)
        };
        var total = t.WM + t.WK + t.BM + t.BK;
        return BitOperations.PopCount(p.Occ) == total;
    }
}
