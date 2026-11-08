using System.Diagnostics;

namespace Avventurina.Core;

public sealed record SearchResult(Mv Best, int Score, int Depth, long Nodes, double Ms, string Note);

/// <summary>
/// Principal variation alpha-beta with iterative deepening, transposition table, killers and history.
/// Captures are forced in draughts, so a node with captures never stands pat. Repetitions are scored as draws
/// using the real game history, which Agata never had.
/// </summary>
public sealed class Searcher
{
    public const int Mate = 30000, MateBound = 29000, MaxPly = 128;
    public bool Reductions { get; set; } = true;
    public int Aspiration { get; set; } = Mate + 1; // off: measured worse on real positions
    public double IterationLimit { get; set; } = 0.7;
    public int Lmr { get; set; } = 1;
    /// <summary>At the horizon, also search quiet moves that offer a capture: draughts shots start with a sacrifice.</summary>
    public bool SacrificeQs { get; set; } = true;
    /// <summary>How many plies past the horizon a chain of sacrifices may continue (0: one sacrifice at the horizon).</summary>
    public int SacPlies { get; set; } = 0;
    /// <summary>Null move with many pieces on the board and no capture in sight (zugzwang is rare there).</summary>
    public bool NullMove { get; set; } = false;
    /// <summary>Centi-men per piece off the board, credited to the side that moves at the root: Avventurina
    /// steers towards simplified positions, where the opponent's deeper search matters less and the tables decide.</summary>
    public int Simplify { get; set; } = 0;
    /// <summary>Value of a draw by rule for the side that moves at the root (positive: draws are welcome).</summary>
    public int DrawValue { get; set; } = 0;
    int rootSide;
    /// <summary>Futility margin per remaining depth (index 1, 2); 0 disables.</summary>
    public int Futility { get; set; } = 0;
    /// <summary>Logarithmic late move reductions instead of the fixed table.</summary>
    public bool LogLmr { get; set; } = false;
    static readonly int[,] LogTable = BuildLog();
    static int[,] BuildLog()
    {
        var t = new int[64, 64];
        for (var d = 1; d < 64; d++) for (var i = 1; i < 64; i++) t[d, i] = (int)(0.5 + Math.Log(d) * Math.Log(i) / 2.0);
        return t;
    }

    readonly SharedTable shared;
    readonly SharedTable.Entry[] table;
    /// <summary>Set by the coordinator of a parallel search to stop helper threads.</summary>
    public StopSignal? Signal { get; set; }
    /// <summary>First iteration depth; helpers start at different depths so the threads diverge.</summary>
    public int StartDepth { get; set; } = 1;
    // static evaluations of recently seen positions (leaves repeat through transpositions)
    const int EvalBits = 18;
    readonly ulong[] evalKeys = new ulong[1 << EvalBits];
    readonly short[] evalValues = new short[1 << EvalBits];
    readonly int mask;
    readonly ulong[] stack = new ulong[1024 + MaxPly];
    readonly Mv[,] killers = new Mv[MaxPly, 2];
    readonly int[,] history = new int[32, 32];
    readonly Mv[][] buffers;
    readonly Tablebase? tablebase;
    int stackBase;
    long nodes;
    public long Cutoffs, FirstCutoffs, CutIndexSum, HorizonNodes, ThreatExtensions, SacNodes, TotalNodes;
    Stopwatch clock = new();
    long deadlineMs;
    bool stop;
    int rootDepth;

    public Searcher(int tableBits = 21, Tablebase? tablebase = null) : this(new SharedTable(tableBits), tablebase) { }

    public Searcher(SharedTable shared, Tablebase? tablebase)
    {
        this.shared = shared;
        table = shared.Entries;
        mask = shared.Mask;
        buffers = new Mv[MaxPly + 1][];
        for (var i = 0; i <= MaxPly; i++) buffers[i] = new Mv[256];
        this.tablebase = tablebase;
    }

    public void Clear()
    {
        shared.Clear();
        Array.Clear(evalKeys);
        Array.Clear(history);
        Array.Clear(killers);
    }

    /// <param name="gameHistory">Zobrist keys of the positions that occurred before the root, oldest first.</param>
    public SearchResult Search(Pos root, IReadOnlyList<ulong> gameHistory, int timeMs, int maxDepth = 64)
    {
        clock = Stopwatch.StartNew();
        deadlineMs = timeMs;
        rootSide = root.Side;
        stop = false;
        nodes = 0;
        var h = Math.Min(gameHistory.Count, 1024);
        for (var i = 0; i < h; i++) stack[i] = gameHistory[gameHistory.Count - h + i];
        stackBase = h;
        // age history scores so they do not dominate across moves
        for (var a = 0; a < 32; a++) for (var b = 0; b < 32; b++) history[a, b] /= 8;

        var rootMoves = buffers[MaxPly];
        var n = MoveGen.Generate(root, rootMoves);
        if (n == 0) throw new InvalidOperationException("Nessuna mossa legale.");
        var moves = rootMoves[..n].ToArray();
        if (n == 1) return new SearchResult(moves[0], 0, 0, 0, clock.Elapsed.TotalMilliseconds, "forzata");

        var best = moves[0];
        int bestScore = 0, completed = 0, aspirationFailures = 0;
        var fullWindowNext = false;
        var scores = new int[n];
        for (var depth = Math.Min(StartDepth, maxDepth); depth <= maxDepth; depth++)
        {
            rootDepth = depth;
            // aspiration window around the previous score; the iteration is repeated with a full window on failure
            var window = !fullWindowNext && depth >= 5 && Math.Abs(bestScore) < MateBound / 2 ? Aspiration : Mate + 1;
            fullWindowNext = false;
            int alpha = Math.Max(-Mate - 1, bestScore - window), beta = Math.Min(Mate + 1, bestScore + window);
            int alphaStart = alpha, betaStart = beta;
            var iterBest = -1;
            var iterScore = -Mate - 1;
            for (var i = 0; i < moves.Length; i++)
            {
                var child = MoveGen.Make(root, moves[i]);
                stack[stackBase] = root.Key;
                int score;
                if (i == 0) score = -Negamax(child, depth - 1, -beta, -alpha, 1);
                else
                {
                    score = -Negamax(child, depth - 1, -alpha - 1, -alpha, 1);
                    if (!stop && score > alpha) score = -Negamax(child, depth - 1, -beta, -alpha, 1);
                }
                if (stop) break;
                scores[i] = score;
                if (score > iterScore) { iterScore = score; iterBest = i; }
                if (score > alpha) alpha = score;
            }
            if (!stop && window <= Mate && (iterScore <= alphaStart || iterScore >= betaStart))
            {
                // outside the window: search this depth again with a full window
                aspirationFailures++;
                depth--;
                fullWindowNext = true;
                continue;
            }
            if (iterBest >= 0 && (!stop || iterScore > bestScore || completed == 0))
            {
                // an aborted iteration is used only for a move that was fully searched and beat the previous best
                if (!stop || iterBest == 0 || iterScore > bestScore) { best = moves[iterBest]; bestScore = iterScore; }
            }
            if (stop) break;
            completed = depth;
            // reorder: best first, the rest by score of this iteration
            var order = Enumerable.Range(0, moves.Length).OrderByDescending(i => i == iterBest ? int.MaxValue : scores[i]).ToArray();
            moves = order.Select(i => moves[i]).ToArray();
            scores = order.Select(i => scores[i]).ToArray();
            if (Math.Abs(bestScore) >= MateBound) break;
            // start another iteration only with enough time left for it to reach at least the first moves;
            // an unfinished iteration still contributes when a fully searched move beats the previous best
            if (clock.ElapsedMilliseconds > deadlineMs * IterationLimit) break;
        }
        return new SearchResult(best, bestScore, completed, nodes, clock.Elapsed.TotalMilliseconds, aspirationFailures > 0 ? $"finestra fallita {aspirationFailures}" : "");
    }

    int Negamax(Pos p, int depth, int alpha, int beta, int ply)
    {
        if ((++nodes & 2047) == 0 && (clock.ElapsedMilliseconds >= deadlineMs || Signal is { Stop: true })) stop = true;
        if (stop) return 0;

        // draws by rule: 40 quiet king moves per side with kings on both sides, repetition (scored at the first
        // recurrence: if a cycle is good for one side, the other side can repeat it until the arbiter stops the game)
        var draw = p.Side == rootSide ? DrawValue : -DrawValue;
        if (p.Quiet >= 80 && p.WK != 0 && p.BK != 0) return draw;
        var sp = stackBase + ply;
        if (p.Quiet >= 4)
            for (var back = 4; back <= p.Quiet && sp - back >= 0; back += 2)
                if (stack[sp - back] == p.Key) return draw;
        stack[sp] = p.Key;

        if (tablebase is not null && p.Count <= tablebase.MaxPieces && tablebase.Probe(p, out var tbScore))
            return tbScore > 0 ? tbScore - ply : tbScore < 0 ? tbScore + ply : 0;

        var moves = buffers[ply];
        var n = MoveGen.Generate(p, moves);
        if (n == 0) return -Mate + ply; // blocked or no pieces: loss
        TotalNodes++;
        if (depth <= 0) HorizonNodes++;
        if (ply >= MaxPly - 1) return Leaf(p);
        var capture = moves[0].IsCapture;

        if (depth <= 0 && !capture)
        {
            // quiet position at the horizon. One extra ply only when the opponent threatens a capture and we are
            // still close to the nominal depth, otherwise stand pat.
            var standPat = Leaf(p);
            if (ply > rootDepth * 2) return standPat;
            if (depth < 0 || !OpponentThreatens(p))
            {
                // beyond the horizon only chains of sacrifices continue, up to SacPlies plies past it
                if (!SacrificeQs || standPat >= beta || depth < -SacPlies) return standPat;
                // shots: a quiet move that forces the opponent to capture, followed by the forced capture chain
                var qbest = standPat;
                if (qbest > alpha) alpha = qbest;
                for (var i = 0; i < n; i++)
                {
                    var child = MoveGen.Make(p, moves[i]);
                    if (!MoveGen.HasCapture(child)) continue;
                    SacNodes++;
                    var score = -Negamax(child, depth - 1, -beta, -alpha, ply + 1);
                    if (stop) return 0;
                    if (score > qbest) { qbest = score; if (score > alpha) { alpha = score; if (alpha >= beta) break; } }
                }
                return qbest;
            }
            ThreatExtensions++;
            depth = 1;
        }

        var alphaOrig = alpha;
        ref var slot = ref table[(int)(p.Key & (ulong)mask)];
        // lockless shared table: the check word is key ^ data ^ captured, so a torn write never reads as a hit
        var e = slot;
        var ttHit = (e.Check ^ e.Data ^ e.Captured) == p.Key;
        int eScore = (short)(e.Data & 0xFFFF), eDepth = (sbyte)(e.Data >> 16 & 0xFF), eFlag = (int)(e.Data >> 24 & 0xFF);
        int eFrom = (int)(e.Data >> 32 & 0xFF), eTo = (int)(e.Data >> 40 & 0xFF);
        if (ttHit && eDepth >= depth && ply > 0)
        {
            var s = FromTable(eScore, ply);
            if (eFlag == 1) return s;
            if (eFlag == 2 && s >= beta) return s;
            if (eFlag == 3 && s <= alpha) return s;
        }

        // pruning at non-PV quiet nodes
        var pv = beta - alpha > 1;
        var staticEval = 0;
        var prunable = !capture && !pv && ply > 0 && Math.Abs(beta) < MateBound;
        if (prunable && (NullMove || Futility > 0)) staticEval = Leaf(p);
        if (prunable && NullMove && depth >= 3 && p.Count >= 12 && staticEval >= beta && !OpponentThreatens(p))
        {
            var q = p;
            q.Side = -p.Side;
            q.Key ^= Zobrist.BlackToMove;
            var r = 2 + depth / 6;
            var ns = -Negamax(q, depth - 1 - r, -beta, -beta + 1, ply + 1);
            if (stop) return 0;
            if (ns >= beta && ns < MateBound) return ns;
        }
        var futile = prunable && Futility > 0 && depth <= 2 && staticEval + Futility * depth <= alpha;

        // ordering
        Span<int> keys = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            ref var m = ref moves[i];
            int k;
            if (ttHit && m.From == eFrom && m.To == eTo && m.Captured == e.Captured) k = 1 << 30;
            else if (m.IsCapture) k = 1 << 28;
            else if (killers[ply, 0].Same(m)) k = 1 << 27;
            else if (killers[ply, 1].Same(m)) k = 1 << 26;
            else k = history[m.From, m.To] + (m.Promotes ? 1 << 25 : 0);
            keys[i] = k;
        }

        // a single legal move is played without spending depth
        var nextDepth = n == 1 ? depth : depth - 1;
        var best = -Mate - 1;
        var bestIndex = 0;
        for (var i = 0; i < n; i++)
        {
            // selection sort step
            var top = i;
            for (var j = i + 1; j < n; j++) if (keys[j] > keys[top]) top = j;
            if (top != i) { (moves[i], moves[top]) = (moves[top], moves[i]); (keys[i], keys[top]) = (keys[top], keys[i]); }

            var child = MoveGen.Make(p, moves[i]);
            if (futile && i > 0 && !moves[i].Promotes && !MoveGen.HasCapture(child)) continue;
            int score;
            if (i == 0) score = -Negamax(child, nextDepth, -beta, -alpha, ply + 1);
            else
            {
                // late quiet moves that are not killers, do not promote and do not leave a capture to the
                // opponent are first searched one ply shallower; a fail high is searched again at full depth
                var reduce = Reductions && depth >= 3 && i >= 2 && !capture && keys[i] < 1 << 26 && !moves[i].Promotes
                             && !MoveGen.HasCapture(child)
                    ? LogLmr ? Math.Max(1, Math.Min(depth - 2, LogTable[Math.Min(depth, 63), Math.Min(i, 63)] - (pv ? 1 : 0)))
                             : 1 + (i >= 5 && depth >= 5 ? 1 : 0) + (Lmr >= 2 && i >= 9 && depth >= 7 ? 1 : 0)
                    : 0;
                score = -Negamax(child, nextDepth - reduce, -alpha - 1, -alpha, ply + 1);
                if (reduce > 0 && score > alpha) score = -Negamax(child, nextDepth, -alpha - 1, -alpha, ply + 1);
                if (score > alpha && score < beta) score = -Negamax(child, nextDepth, -beta, -alpha, ply + 1);
            }
            if (stop) return 0;
            if (score > best) { best = score; bestIndex = i; }
            if (score > alpha) alpha = score;
            if (alpha >= beta)
            {
                Cutoffs++; if (i == 0) FirstCutoffs++; CutIndexSum += i;
                if (!moves[i].IsCapture)
                {
                    if (!killers[ply, 0].Same(moves[i])) { killers[ply, 1] = killers[ply, 0]; killers[ply, 0] = moves[i]; }
                    history[moves[i].From, moves[i].To] += depth * depth;
                }
                break;
            }
        }

        var bm = moves[bestIndex];
        var flag = best <= alphaOrig ? 3UL : best >= beta ? 2UL : 1UL;
        var data = (ulong)(ushort)ToTable(best, ply) | (ulong)(byte)(sbyte)Math.Clamp(depth, -1, 120) << 16 | flag << 24
                 | (ulong)bm.From << 32 | (ulong)bm.To << 40;
        slot.Data = data;
        slot.Captured = bm.Captured;
        slot.Check = p.Key ^ data ^ bm.Captured;
        return best;
    }

    int Leaf(in Pos p)
    {
        var v = CachedEval(p);
        if (Simplify == 0) return v;
        var bonus = Simplify * (24 - p.Count);
        return p.Side == rootSide ? v + bonus : v - bonus;
    }

    int CachedEval(in Pos p)
    {
        var i = (int)(p.Key >> (64 - EvalBits));
        if (evalKeys[i] == p.Key) return evalValues[i];
        var v = Eval.Evaluate(p);
        evalKeys[i] = p.Key; evalValues[i] = (short)v;
        return v;
    }

    static bool OpponentThreatens(in Pos p)
    {
        var q = p;
        q.Side = -p.Side;
        return MoveGen.HasCapture(q);
    }

    static short ToTable(int s, int ply) => (short)(s >= MateBound ? s + ply : s <= -MateBound ? s - ply : s);
    static int FromTable(int s, int ply) => s >= MateBound ? s - ply : s <= -MateBound ? s + ply : s;
}

/// <summary>Transposition table that several searchers can share (lockless, verified by a check word).</summary>
public sealed class SharedTable
{
    public struct Entry
    {
        public ulong Check, Data;
        public uint Captured;
    }

    public readonly Entry[] Entries;
    public readonly int Mask;

    public SharedTable(int bits)
    {
        Entries = new Entry[1 << bits];
        Mask = (1 << bits) - 1;
    }

    public void Clear() => Array.Clear(Entries);
}

public sealed class StopSignal { public volatile bool Stop; }

/// <summary>
/// Lazy parallel search: every thread searches the same root on a shared table, helpers starting from staggered
/// depths. The table carries what one thread learns to the others. The deepest completed result is played.
/// </summary>
public sealed class ParallelSearcher
{
    public IReadOnlyList<Searcher> All { get; }
    readonly SharedTable table;

    public ParallelSearcher(int threads, int tableBits, Tablebase? tablebase)
    {
        table = new SharedTable(tableBits);
        All = Enumerable.Range(0, Math.Max(1, threads)).Select(i => new Searcher(table, tablebase) { StartDepth = 1 + i % 3 }).ToArray();
    }

    public void Clear() { foreach (var s in All) s.Clear(); }

    public SearchResult Search(Pos root, IReadOnlyList<ulong> history, int timeMs)
    {
        if (All.Count == 1) return All[0].Search(root, history, timeMs);
        var signal = new StopSignal();
        var results = new SearchResult?[All.Count];
        var threads = new List<Thread>();
        for (var i = 1; i < All.Count; i++)
        {
            var k = i;
            All[k].Signal = signal;
            var t = new Thread(() => results[k] = All[k].Search(root, history, timeMs)) { IsBackground = true };
            threads.Add(t);
            t.Start();
        }
        results[0] = All[0].Search(root, history, timeMs);
        signal.Stop = true;
        foreach (var t in threads) t.Join();
        var best = results[0]!;
        long nodes = results.Sum(r => r?.Nodes ?? 0);
        foreach (var r in results)
            if (r is not null && r.Depth > best.Depth && Math.Abs(r.Score) < Searcher.MateBound) best = r;
        return best with { Nodes = nodes, Note = $"{All.Count} thread" };
    }
}
