using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Avventurina.Core;
using Dama.Competition;
using Dama.Engine;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
switch (args.FirstOrDefault())
{
    case "check": Check(int.Parse(args.ElementAtOrDefault(1) ?? "2000")); break;
    case "bench": Bench(int.Parse(args.ElementAtOrDefault(1) ?? "1000")); break;
    case "tactics": Tactics(args[1], int.Parse(args.ElementAtOrDefault(2) ?? "1000")); break;
    case "prof": Prof(); break;
    case "label": Label(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), args.Skip(5).ToArray()); break;
    case "features": Features(args[1], args.Skip(2).ToArray()); break;
    case "policy": Policy(args[1], args[2]); break;
    case "interrupt": MarkInterrupted(args[1], args.Skip(2).ToArray()); break;
    case "pdn": Pdn(args[1], args[2] == "mirror", args.Skip(3).ToArray()); break;
    case "pbench": PBench(args[1], int.Parse(args[2]), int.Parse(args[3])); break;
    case "benchgames": BenchGames(args[1], int.Parse(args.ElementAtOrDefault(2) ?? "1000")); break;
    case "match": await Match(args.Skip(1).ToArray()); break;
    case "tbverify": TbVerify(int.Parse(args.ElementAtOrDefault(1) ?? "400"), int.Parse(args.ElementAtOrDefault(2) ?? "7")); break;
    case "tb": TbReport(int.Parse(args.ElementAtOrDefault(1) ?? "4")); break;
    default: Console.WriteLine("uso: check [cammini] | bench [ms] | match --out dir --games N --seed S --source test|benchmark [--think 1100]"); break;
}

// Compares the bitboard generator with the shared arbiter on positions from random legal walks.
// Nothing is recorded: these are synthetic walks, no model plays.
static void Check(int walks)
{
    var random = new Random(12345);
    long positions = 0, moves = 0;
    var buffer = new Mv[256];
    for (var w = 0; w < walks; w++)
    {
        var p = Position.Initial();
        for (var ply = 0; ply < 300; ply++)
        {
            var legal = Rules.LegalMoves(p);
            var bp = Pos.FromBoard(p.Board, p.Turn, p.QuietKingPlyCount);
            var n = MoveGen.Generate(bp, buffer);
            var mine = buffer[..n].Select(m => $"{m.From}-{m.To}:{m.Captured}").ToHashSet();
            var theirs = legal.Select(m => $"{m.From}-{m.To}:{CapMask(m)}").ToHashSet();
            if (!mine.SetEquals(theirs))
                throw new Exception($"Differenza in [{string.Join(',', p.Board)}] turno {p.Turn}: mie {string.Join(' ', mine.Order())} arbitro {string.Join(' ', theirs.Order())}");
            var quietCount = MoveGen.Quiets(bp, new Mv[256]);
            var bitCount = 0;
            {
                var empty = ~bp.Occ;
                uint men = bp.Side > 0 ? bp.WM : bp.BM, kings = bp.Side > 0 ? bp.WK : bp.BK;
                bitCount = System.Numerics.BitOperations.PopCount(Eval.UpLeft(kings) & empty) + System.Numerics.BitOperations.PopCount(Eval.UpRight(kings) & empty)
                    + System.Numerics.BitOperations.PopCount(Eval.DownLeft(kings) & empty) + System.Numerics.BitOperations.PopCount(Eval.DownRight(kings) & empty)
                    + (bp.Side > 0 ? System.Numerics.BitOperations.PopCount(Eval.UpLeft(men) & empty) + System.Numerics.BitOperations.PopCount(Eval.UpRight(men) & empty)
                                   : System.Numerics.BitOperations.PopCount(Eval.DownLeft(men) & empty) + System.Numerics.BitOperations.PopCount(Eval.DownRight(men) & empty));
            }
            if (quietCount != bitCount) throw new Exception($"mobilità a bit {bitCount} diversa da {quietCount}");
            if (MoveGen.HasCapture(bp) != (legal.Count > 0 && legal[0].Captures.Length > 0)) throw new Exception("HasCapture diverge");
            positions++; moves += legal.Count;
            if (legal.Count == 0) break;
            var move = legal[random.Next(legal.Count)];
            var next = Rules.Apply(p, move);
            var bm = buffer[..n].First(m => m.From == move.From && m.To == move.To && m.Captured == CapMask(move));
            var bn = MoveGen.Make(bp, bm);
            if (!bn.ToBoard().SequenceEqual(next.Board) || bn.Quiet != next.QuietKingPlyCount || bn.Side != next.Turn || bn.Key != Zobrist.Compute(bn))
                throw new Exception($"Make diverge: board {!bn.ToBoard().SequenceEqual(next.Board)} quiet {bn.Quiet}/{next.QuietKingPlyCount} side {bn.Side}/{next.Turn} key {bn.Key != Zobrist.Compute(bn)} mossa {move.Id} [{string.Join(',', p.Board)}] -> [{string.Join(',', bn.ToBoard())}] vs [{string.Join(',', next.Board)}]");
            p = next;
        }
    }
    // lesson from Agata: crowning must always gain, for every promotion square and phase
    var worst = int.MaxValue;
    foreach (var extra in new[] { 0, 6, 16 })
        for (var s = 4; s < 8; s++)
            for (var d = 0; d < 2; d++)
            {
                var t = Geometry.Step[s, d];
                if (t < 0) continue;
                var board = Filler(extra);
                if (board[s] != 0 || board[t] != 0) continue;
                board[s] = 1;
                var before = Eval.White(Pos.FromBoard(board, -1, 0));
                board[s] = 0; board[t] = 2;
                worst = Math.Min(worst, Eval.White(Pos.FromBoard(board, -1, 0)) - before);
            }
    Console.WriteLine($"check ok: {walks} cammini casuali, {positions} posizioni, {moves} mosse identiche all'arbitro; guadagno minimo della promozione {worst}");
    if (worst < 25) throw new Exception("La promozione non guadagna abbastanza: errore di Agata ripetuto.");
}

// symmetric material far from the promotion row, so only the crowning man changes
static int[] Filler(int pieces)
{
    var b = new int[32];
    b[31] = -2;
    for (var i = 0; i < pieces / 2; i++) { b[16 + i] = 1; b[8 + i] = -1; }
    return b;
}

// Builds the tables and prints a few results that can be checked by hand.
static void TbReport(int pieces)
{
    var tb = Tablebase.LoadOrBuild(Path.Combine(Path.GetTempPath(), "avventurina-tb"), pieces, Console.WriteLine);
    void Show(string name, params (int sq, int piece)[] items)
    {
        var b = new int[32]; foreach (var (sq, piece) in items) b[sq - 1] = piece;
        foreach (var side in new[] { 1, -1 })
        {
            var ok = tb.Probe(Pos.FromBoard(b, side, 0), out var score);
            Console.WriteLine($"{name}, muove {(side > 0 ? "Bianco" : "Nero")}: {(ok ? score == 0 ? "patta" : score > 0 ? $"vince in {Tablebase.WinScore - score}" : $"perde in {Tablebase.WinScore + score}" : "non in tabella")}");
        }
    }
    Show("dama contro dama lontana", (1, 2), (32, -2));
    Console.WriteLine($"  controllo arbitro, dama contro dama, muove Bianco, entro 11: {Solve(new Position(Board((1, 2), (32, -2)), 1), 11)}");
    Show("due dame contro una", (1, 2), (2, 2), (32, -2));
    Show("pedina in 2a contro dama lontana", (6, 1), (32, -2));
    Show("dama e pedina contro dama", (18, 2), (22, 1), (3, -2));
}

// Independent check: exact minimax with the shared arbiter (not the bitboard code) on random table positions.
static void TbVerify(int samples, int maxDepth)
{
    var tb = Tablebase.LoadOrBuild(Path.Combine(Path.GetTempPath(), "avventurina-tb"), 4, null);
    var random = new Random(777);
    int checkedWins = 0, checkedOther = 0;
    while (checkedWins + checkedOther < samples)
    {
        var b = new int[32];
        var count = random.Next(2, 5);
        var whites = random.Next(1, count);
        for (var i = 0; i < count; i++)
        {
            int sq; do sq = random.Next(32); while (b[sq] != 0);
            var white = i < whites;
            var king = random.Next(2) == 0 || (white ? sq < 4 : sq >= 28);
            b[sq] = (white ? 1 : -1) * (king ? 2 : 1);
        }
        var side = random.Next(2) == 0 ? 1 : -1;
        if (!tb.Probe(Pos.FromBoard(b, side, 0), out var score)) continue;
        var d = score == 0 ? -1 : Tablebase.WinScore - Math.Abs(score);
        var p = new Position(b, side);
        if (d >= 0 && d <= maxDepth)
        {
            var exact = Solve(p, d);   // must be decided within d plies with the same result
            var expected = score > 0 ? 1 : -1;
            if (exact != expected) throw new Exception($"tabella {score} ma minimax {exact} a profondità {d}: [{string.Join(',', b)}] turno {side}");
            checkedWins++;
        }
        else if (d < 0)
        {
            if (Solve(p, maxDepth) != 0) throw new Exception($"tabella patta ma minimax decide entro {maxDepth}: [{string.Join(',', b)}] turno {side}");
            checkedOther++;
        }
    }
    Console.WriteLine($"tabelle verificate con l'arbitro: {checkedWins} vittorie/sconfitte decise entro la distanza della tabella, {checkedOther} patte non decise entro {maxDepth} semimosse");
}

// +1 side to move wins within depth, -1 loses within depth, 0 undecided
static int Solve(Position p, int depth)
{
    var legal = Rules.LegalMoves(p);
    if (legal.Count == 0) return -1;
    if (depth == 0) return 0;
    var best = -1;
    foreach (var m in legal)
    {
        var r = -Solve(Rules.Apply(p, m), depth - 1);
        if (r > best) best = r;
        if (best == 1) break;
    }
    return best;
}

static int[] Board(params (int sq, int piece)[] items) { var b = new int[32]; foreach (var (sq, piece) in items) b[sq - 1] = piece; return b; }

static uint CapMask(Move m) => m.Captures.Aggregate(0u, (a, s) => a | 1u << s);

static void Bench(int ms)
{
    var s = new Searcher();
    var p = Position.Initial();
    var r = s.Search(Pos.FromBoard(p.Board, 1, 0), [], ms);
    Console.WriteLine($"iniziale: mossa {r.Best.From + 1}-{r.Best.To + 1} profondità {r.Depth} valutazione {r.Score} nodi {r.Nodes} in {r.Ms:F0} ms ({r.Nodes / Math.Max(1, r.Ms) * 1000:F0} nodi/s)");
}

// Searches the positions where Avventurina had to move in recorded games (no new game is played) and prints
// average depth, so search changes can be compared on real middle-game positions.
static void BenchGames(string path, int ms)
{
    var s = new Searcher(21, Tablebase.LoadOrBuild(null, 5));
    if (Environment.GetEnvironmentVariable("AVV_ASP") is { } asp) s.Aspiration = int.Parse(asp);
    if (Environment.GetEnvironmentVariable("AVV_NOSAC") is not null) s.SacrificeQs = false;
    if (Environment.GetEnvironmentVariable("AVV_SACPLIES") is { } sp) s.SacPlies = int.Parse(sp);
    if (Environment.GetEnvironmentVariable("AVV_NULL") is not null) s.NullMove = true;
    if (Environment.GetEnvironmentVariable("AVV_LOG") is not null) s.LogLmr = true;
    if (Environment.GetEnvironmentVariable("AVV_FUT") is { } fut) s.Futility = int.Parse(fut);
    if (Environment.GetEnvironmentVariable("AVV_ITER") is { } it) s.IterationLimit = double.Parse(it, System.Globalization.CultureInfo.InvariantCulture);
    var depths = new List<int>();
    long nodes = 0; double time = 0;
    foreach (var line in File.ReadLines(path))
    {
        using var doc = JsonDocument.Parse(line);
        foreach (var f in doc.RootElement.GetProperty("frames").EnumerateArray())
        {
            var ply = f.GetProperty("ply").GetInt32();
            if (ply < 10 || ply > 40 || ply % 4 != 0) continue;
            var board = f.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var turn = f.GetProperty("turn").GetInt32();
            s.Clear();
            var r = s.Search(Pos.FromBoard(board, turn, 0), [], ms);
            if (r.Depth == 0) continue;
            depths.Add(r.Depth); nodes += r.Nodes; time += r.Ms;
        }
    }
    Console.WriteLine($"{depths.Count} posizioni: profondità media {depths.Average():F1}, minima {depths.Min()}, {nodes / time * 1000:F0} nodi/s, {time / depths.Count:F0} ms medi");
    Console.WriteLine($"nodi interni {s.TotalNodes}, all'orizzonte {100.0 * s.HorizonNodes / Math.Max(1, s.TotalNodes):F1}%, estensioni per minaccia {100.0 * s.ThreatExtensions / Math.Max(1, s.TotalNodes):F1}%, sacrifici provati {100.0 * s.SacNodes / Math.Max(1, s.TotalNodes):F1}%");
    Console.WriteLine($"tagli {s.Cutoffs}, alla prima mossa {100.0 * s.FirstCutoffs / Math.Max(1, s.Cutoffs):F1}%, indice medio {(double)s.CutIndexSum / Math.Max(1, s.Cutoffs):F2}");
}

// Positions from recorded games where Avventurina's own score fell by at least 70 within its next two moves
// (a danger it did not see in time). A version "sees" the danger if, searching the earlier position, it scores
// it at least 50 lower than the recorded score or picks another move. No game is played.
static void Tactics(string path, int ms)
{
    var s = new Searcher(21, Tablebase.LoadOrBuild(null, 5));
    if (Environment.GetEnvironmentVariable("AVV_ITER") is { } it) s.IterationLimit = double.Parse(it, System.Globalization.CultureInfo.InvariantCulture);
    if (Environment.GetEnvironmentVariable("AVV_NOSAC") is not null) s.SacrificeQs = false;
    if (Environment.GetEnvironmentVariable("AVV_SACPLIES") is { } sp) s.SacPlies = int.Parse(sp);
    if (Environment.GetEnvironmentVariable("AVV_NULL") is not null) s.NullMove = true;
    if (Environment.GetEnvironmentVariable("AVV_LOG") is not null) s.LogLmr = true;
    if (Environment.GetEnvironmentVariable("AVV_FUT") is { } fut) s.Futility = int.Parse(fut);
    if (Environment.GetEnvironmentVariable("AVV_LMR") is { } lmr) s.Lmr = int.Parse(lmr);
    if (Environment.GetEnvironmentVariable("AVV_NOLMR") is not null) s.Reductions = false;
    int total = 0, seen = 0, other = 0;
    var depths = new List<int>();
    foreach (var line in File.ReadLines(path))
    {
        using var doc = JsonDocument.Parse(line);
        var frames = doc.RootElement.GetProperty("frames").EnumerateArray().ToArray();
        var mine = new List<(int idx, int score, string move)>();
        for (var i = 1; i < frames.Length; i++)
        {
            if (frames[i].GetProperty("by").GetString() != "avventurina") continue;
            var c = frames[i].TryGetProperty("comment", out var cm) ? cm.GetString() ?? "" : "";
            var m = System.Text.RegularExpressions.Regex.Match(c, @"valutazione (-?\d+)");
            if (!m.Success || c.Contains("profondità 0")) continue;
            mine.Add((i, int.Parse(m.Groups[1].Value), frames[i].GetProperty("moveId").GetString()!));
        }
        for (var k = 0; k + 2 < mine.Count; k++)
        {
            var (idx, score, move) = mine[k];
            if (score < -40 || Math.Abs(score) > 1000) continue;
            if (Math.Min(mine[k + 1].score, mine[k + 2].score) > score - 70) continue;
            var before = frames[idx - 1];
            var board = before.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var turn = before.GetProperty("turn").GetInt32();
            s.Clear();
            var r = s.Search(Pos.FromBoard(board, turn, 0), [], ms);
            var newMove = $"{r.Best.From + 1}";
            total++;
            depths.Add(r.Depth);
            var different = !move.StartsWith(newMove + "-") || !move.EndsWith("-" + (r.Best.To + 1));
            if (r.Score <= score - 50) seen++;
            else if (different) other++;
        }
    }
    Console.WriteLine($"{total} pericoli: visti in anticipo {seen}, mossa diversa senza vederli {other}, profondità media {(depths.Count > 0 ? depths.Average() : 0):F1}");
}

// Average completed depth with N threads on the positions of recorded games (no game is played).
static void PBench(string path, int threads, int ms)
{
    var ps = new ParallelSearcher(threads, 22, Tablebase.LoadOrBuild(null, 5));
    var depths = new List<int>(); long nodes = 0; double time = 0;
    foreach (var line in File.ReadLines(path))
    {
        using var doc = JsonDocument.Parse(line);
        foreach (var f in doc.RootElement.GetProperty("frames").EnumerateArray())
        {
            var ply = f.GetProperty("ply").GetInt32();
            if (ply < 10 || ply > 40 || ply % 4 != 0) continue;
            var board = f.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            ps.Clear();
            var r = ps.Search(Pos.FromBoard(board, f.GetProperty("turn").GetInt32(), 0), [], ms);
            if (r.Depth == 0) continue;
            depths.Add(r.Depth); nodes += r.Nodes; time += r.Ms;
        }
    }
    Console.WriteLine($"{threads} thread, {depths.Count} posizioni: profondità media {depths.Average():F1}, {nodes / time * 1000:F0} nodi/s totali");
}

// Writes one row per quiet position of the recorded games: result from White's view, phase, features.
// Reads games already played and registered; plays nothing.
static void Features(string output, string[] patterns)
{
    using var w = new StreamWriter(output);
    Span<float> f = stackalloc float[Tuned.Count];
    long rows = 0, games = 0;
    var files = patterns.SelectMany(p => Directory.GetFiles(Path.GetDirectoryName(p)!.Length == 0 ? "." : Path.GetDirectoryName(p)!, "games.jsonl", SearchOption.AllDirectories)).Distinct().ToArray();
    foreach (var file in files)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); } catch (IOException) { Console.WriteLine($"saltato (in uso): {file}"); continue; }
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("completed", out var c) && c.ValueKind == JsonValueKind.False) continue;
            var winner = root.GetProperty("winner").ValueKind == JsonValueKind.Null ? null : root.GetProperty("winner").GetString();
            var white = root.GetProperty("white").GetString();
            var result = winner is null ? 0.5 : winner == white ? 1.0 : 0.0;
            games++;
            foreach (var fr in root.GetProperty("frames").EnumerateArray())
            {
                if (fr.GetProperty("ply").GetInt32() < 6) continue;
                var board = fr.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                var p = Pos.FromBoard(board, fr.GetProperty("turn").GetInt32(), 0);
                if (MoveGen.HasCapture(p) || p.Count <= 5) continue;
                Tuned.Features(p, f);
                var sb = new System.Text.StringBuilder();
                sb.Append(result.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(Tuned.Phase(p));
                for (var i = 0; i < Tuned.Count; i++) sb.Append(',').Append(f[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                w.WriteLine(sb);
                rows++;
            }
        }
    }
    Console.WriteLine($"{games} partite, {rows} posizioni tranquille scritte in {output}");
}

// Labels quiet positions of recorded games with a short search of Avventurina's own engine (hand evaluation):
// result from White's view, search score from White's view, phase, features. Plays no game.
static void Label(string output, int ms, int threads, int every, string[] dirs)
{
    var positions = new List<(double result, Pos p)>();
    foreach (var dir in dirs)
        foreach (var file in Directory.GetFiles(dir, "games.jsonl", SearchOption.AllDirectories))
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch (IOException) { continue; }
            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("completed", out var c) && c.ValueKind == JsonValueKind.False) continue;
                var winner = root.GetProperty("winner").ValueKind == JsonValueKind.Null ? null : root.GetProperty("winner").GetString();
                var result = winner is null ? 0.5 : winner == root.GetProperty("white").GetString() ? 1.0 : 0.0;
                var k = 0;
                foreach (var fr in root.GetProperty("frames").EnumerateArray())
                {
                    if (fr.GetProperty("ply").GetInt32() < 6) continue;
                    var p = Pos.FromBoard(fr.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).ToArray(), fr.GetProperty("turn").GetInt32(), 0);
                    if (MoveGen.HasCapture(p) || p.Count <= 5) continue;
                    if (k++ % every == 0) positions.Add((result, p));
                }
            }
        }
    Console.WriteLine($"{positions.Count} posizioni da etichettare, {threads} thread, {ms} ms ciascuna");
    var rows = new string[positions.Count];
    var tb = Tablebase.LoadOrBuild(null, 5);
    var done = 0;
    var clock = Stopwatch.StartNew();
    Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
    {
        var searcher = new Searcher(18, tb);
        Span<float> f = stackalloc float[Tuned.Count];
        for (var i = t; i < positions.Count; i += threads)
        {
            var (result, p) = positions[i];
            searcher.Clear();
            var n = MoveGen.Generate(p, new Mv[256]);
            if (n == 0) continue;
            var r = searcher.Search(p, [], ms);
            var white = p.Side > 0 ? r.Score : -r.Score;
            Tuned.Features(p, f);
            var sb = new System.Text.StringBuilder();
            sb.Append(result.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(white).Append(',').Append(Tuned.Phase(p));
            for (var j = 0; j < Tuned.Count; j++) sb.Append(',').Append(f[j].ToString(System.Globalization.CultureInfo.InvariantCulture));
            rows[i] = sb.ToString();
            var d = Interlocked.Increment(ref done);
            if (d % 5000 == 0) Console.WriteLine($"  {d} in {clock.Elapsed.TotalSeconds:F0} s");
        }
    });
    File.WriteAllLines(output, rows.Where(r => r is not null));
    Console.WriteLine($"scritte {rows.Count(r => r is not null)} righe in {output}");
}

static void Prof()
{
    var random = new Random(9);
    var list = new List<Pos>();
    var buf = new Mv[256];
    while (list.Count < 200000)
    {
        var p = Pos.FromBoard(Position.Initial().Board, 1, 0);
        for (var ply = 0; ply < 120; ply++)
        {
            var n = MoveGen.Generate(p, buf);
            if (n == 0) break;
            list.Add(p);
            p = MoveGen.Make(p, buf[random.Next(n)]);
        }
    }
    long sink = 0;
    var sw = Stopwatch.StartNew();
    for (var r = 0; r < 5; r++) foreach (var p in list) sink += MoveGen.Generate(p, buf);
    var gen = sw.Elapsed.TotalMilliseconds; sw.Restart();
    for (var r = 0; r < 5; r++) foreach (var p in list) sink += Eval.Evaluate(p);
    var ev = sw.Elapsed.TotalMilliseconds; sw.Restart();
    for (var r = 0; r < 5; r++) foreach (var p in list) sink += MoveGen.HasCapture(p) ? 1 : 0;
    var hc = sw.Elapsed.TotalMilliseconds; sw.Restart();
    for (var r = 0; r < 5; r++) foreach (var p in list) { var n = MoveGen.Generate(p, buf); if (n > 0) sink += (long)MoveGen.Make(p, buf[0]).Key; }
    var mk = sw.Elapsed.TotalMilliseconds - gen;
    var c = list.Count * 5 / 1000.0;
    long evalSum = 0; foreach (var p in list) evalSum = evalSum * 31 + Eval.Evaluate(p);
    Console.WriteLine($"somma di controllo valutazione {evalSum}");
    Console.WriteLine($"ns per chiamata: genera {gen / c * 1000:F0}, valuta {ev / c * 1000:F0}, presa? {hc / c * 1000:F0}, esegui {mk / c * 1000:F0} ({sink % 7})");
}

async Task Match(string[] a)
{
    string Arg(string name, string fallback) { var i = Array.IndexOf(a, name); return i >= 0 ? a[i + 1] : fallback; }
    var output = Path.GetFullPath(Arg("--out", "match-" + DateTime.Now.ToString("HHmmss")));
    var games = int.Parse(Arg("--games", "2"));
    var seed = int.Parse(Arg("--seed", "1"));
    var source = Arg("--source", "test");
    var think = int.Parse(Arg("--think", "1100"));
    var mine = new Player("avventurina", "Avventurina", Arg("--me", "http://127.0.0.1:9021/choose-move"));
    var rival = new Player(Arg("--rival-id", "kingsrow"), Arg("--rival-name", "Kingsrow Italian 1.19e"), Arg("--rival", "http://127.0.0.1:9025/choose-move"));
    if (source is not ("test" or "benchmark")) throw new ArgumentException("source: test o benchmark");
    if (Directory.Exists(output)) throw new InvalidOperationException("Directory risultati già esistente: non sovrascrivo.");
    Directory.CreateDirectory(output);
    var root = FindRoot();
    var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
    {
        startedAt = DateTimeOffset.UtcNow, games, seed, source, thinkTimeMs = think, openingPlies = 6, plyLimit = 400,
        avventurina = await Health(http, mine.Endpoint.Replace("/choose-move", "/health")),
        rival = await Health(http, rival.Endpoint.Replace("/choose-move", "/health")),
        competitionDirectory = store.DirectoryPath, lockFile = Arg("--lock", "matches-9025.lock"), affinity = Arg("--affinity", "Avventurina e la sua copia Kingsrow sul processore logico 4")
    }, new JsonSerializerOptions(json) { WriteIndented = true }));
    using var log = new StreamWriter(Path.Combine(output, "games.jsonl"));
    int w = 0, d = 0, l = 0, x = 0;
    for (var n = 0; n < games; n++)
    {
        using var lockFile = await Lock(Path.Combine(root, "arena", "kingsrow-challenge", Arg("--lock", "matches-9025.lock")));
        var white = n % 2 == 0 ? mine : rival;
        var black = n % 2 == 0 ? rival : mine;
        using var tracked = store.Start(source, Participant.Ai(white.Id, white.Name), Participant.Ai(black.Id, black.Name));
        var p = Position.Initial();
        var seen = new Dictionary<string, int> { [p.Key] = 1 };
        var history = new List<object>();
        var frames = new List<object> { new { ply = 0, board = p.Board, turn = p.Turn } };
        var random = new Random(seed + n / 2);
        Outcome? outcome = null;
        string? error = null;
        try
        {
            while (true)
            {
                var legal = Rules.LegalMoves(p);
                outcome = Rules.GetOutcome(p, seen.GetValueOrDefault(p.Key), legal);
                if (outcome is not null || p.Ply >= 400) break;
                var player = p.Turn == 1 ? white : black;
                var clock = Stopwatch.StartNew();
                Move move; string? comment = null;
                if (p.Ply < 6) move = legal[random.Next(legal.Count)];
                else (move, comment) = await Ask(http, player, p, legal, tracked.Id, history, seen.GetValueOrDefault(p.Key), think);
                history.Add(new { ply = p.Ply + 1, player = p.Turn == 1 ? "white" : "black", moveId = move.Id });
                var by = p.Ply < 6 ? "opening" : player.Id;
                p = Rules.Apply(p, move);
                seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                frames.Add(new { ply = p.Ply, board = p.Board, turn = p.Turn, moveId = move.Id, by, ms = Math.Round(clock.Elapsed.TotalMilliseconds), comment });
            }
        }
        catch (Exception e) { error = e.Message; }
        if (outcome is not null) tracked.Complete(outcome.Winner, p.Ply, outcome.Reason);
        else tracked.Interrupt(p.Ply, error is null ? "ply-limit" : "model-failure");
        var winnerId = outcome is null || outcome.Winner == 0 ? null : outcome.Winner == 1 ? white.Id : black.Id;
        if (outcome is null) x++; else if (winnerId is null) d++; else if (winnerId == mine.Id) w++; else l++;
        log.WriteLine(JsonSerializer.Serialize(new { matchId = tracked.Id, n, openingSeed = seed + n / 2, white = white.Id, black = black.Id, completed = outcome is not null, winner = winnerId, reason = outcome?.Reason ?? (error is null ? "ply-limit" : "model-failure"), plies = p.Ply, error, frames }, json));
        await log.FlushAsync();
        Console.WriteLine($"{n + 1}/{games} {white.Id}-{black.Id}: {(outcome is null ? "interrotta " + error : winnerId ?? "patta")} ({p.Ply} ply, {outcome?.Reason})  Avventurina V{w} P{d} S{l} I{x}");
        if (error is not null) break;
    }
    File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { wins = w, draws = d, losses = l, interrupted = x, score = (w + d / 2.0) / Math.Max(1, w + d + l) }, json));
}

async Task<(Move, string?)> Ask(HttpClient http, Player player, Position p, List<Move> legal, string id, List<object> history, int repetitions, int think)
{
    var side = p.Turn == 1 ? "white" : "black";
    var request = new
    {
        protocolVersion = "dama-model/v1", rules = "italian-draughts-8x8", requestId = Guid.NewGuid().ToString("N"), gameId = id, player = side,
        position = new { board = p.Board, turn = side, ply = p.Ply, quietKingPlyCount = p.QuietKingPlyCount, repetitionCount = repetitions },
        legalMoves = legal.Select(m => new { id = m.Id, from = m.From + 1, to = m.To + 1, path = m.Path.Select(s => s + 1), captures = m.Captures.Select(s => s + 1), promotes = m.Promotes }),
        recentMoves = history.TakeLast(16), thinkTimeMs = think
    };
    using var response = await http.PostAsJsonAsync(player.Endpoint, request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{player.Id}: HTTP {(int)response.StatusCode}: {text}");
    var answer = JsonSerializer.Deserialize<Answer>(text, json);
    return answer?.ProtocolVersion == "dama-model/v1" && legal.Find(m => m.Id == answer.MoveId) is { } found ? (found, answer.Comment)
        : throw new InvalidOperationException($"Risposta illegale di {player.Id}: {text}");
}

static async Task<object?> Health(HttpClient http, string url) => JsonSerializer.Deserialize<JsonElement>(await http.GetStringAsync(url));

static async Task<FileStream> Lock(string path)
{
    while (true)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException) { await Task.Delay(500); }
    }
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "WeightSpaceGames.slnx"))) return dir.FullName;
    throw new InvalidOperationException("Radice del repository non trovata.");
}



// Converts PDN game collections (Italian draughts, squares 1..32) into games.jsonl-like records, replaying every move
// with Avventurina's generator. Only moves and results are read; comments and annotations are ignored.
static void Pdn(string output, bool mirror, string[] files)
{
    int ToSq(int n) { var s = n - 1; return mirror ? s / 4 * 4 + 3 - s % 4 : s; }
    var json = new JsonSerializerOptions();
    using var log = new StreamWriter(output);
    int games = 0, bad = 0, moves = 0;
    foreach (var file in files)
    {
        var text = File.ReadAllText(file, System.Text.Encoding.Latin1);
        foreach (var chunk in System.Text.RegularExpressions.Regex.Split(text, @"(?=\[Event )"))
        {
            var rm = System.Text.RegularExpressions.Regex.Match(chunk, @"\[Result ""([^""]*)""\]");
            if (!rm.Success) continue;
            var result = rm.Groups[1].Value;
            string? winner = result.StartsWith("1-0") || result.StartsWith("2-0") ? "white" : result.StartsWith("0-1") || result.StartsWith("0-2") ? "black" : result.StartsWith("1/2") || result.StartsWith("1-1") ? null : "?";
            if (winner == "?") continue;
            var body = System.Text.RegularExpressions.Regex.Replace(chunk, @"\[[^\]]*\]", " ");
            body = System.Text.RegularExpressions.Regex.Replace(body, @"\{[^}]*\}", " ");
            body = System.Text.RegularExpressions.Regex.Replace(body, @"(1-0|0-1|2-0|0-2|1/2-1/2|1-1|\*)\s*$", " ");
            body = System.Text.RegularExpressions.Regex.Replace(body, @"\d+\.", " ");
            var tokens = System.Text.RegularExpressions.Regex.Matches(body, @"(?<![\d/])\d{1,2}(?:[-x]\d{1,2})+(?![\d/])").Select(m => m.Value).ToList();
            if (tokens.Count < 4) continue;
            var start = new int[32];
            for (var q = 0; q < 12; q++) { start[q] = -1; start[31 - q] = 1; }
            var p = Pos.FromBoard(start, 1, 0);
            var frames = new List<object>();
            var ok = true; var buf = new Mv[256];
            foreach (var t in tokens)
            {
                var parts = t.Split('-', 'x').Select(int.Parse).ToArray();
                if (parts.Any(x => x < 1 || x > 32)) { ok = false; break; }
                int from = ToSq(parts[0]), to = ToSq(parts[^1]);
                var n = MoveGen.Generate(p, buf);
                var idx = -1;
                for (var i = 0; i < n; i++) if (buf[i].From == from && buf[i].To == to) { idx = i; break; }
                if (idx < 0) { ok = false; break; }
                frames.Add(new { ply = frames.Count, turn = p.Side, board = p.ToBoard(), move = t });
                p = MoveGen.Make(p, buf[idx]);
            }
            if (!ok) { bad++; continue; }
            games++; moves += frames.Count;
            log.WriteLine(JsonSerializer.Serialize(new { source = Path.GetFileName(file), white = "white", black = "black", completed = true, winner, plies = frames.Count, frames }, json));
        }
    }
    Console.WriteLine($"{games} partite valide, {bad} scartate, {moves} posizioni");
}


// Comparison-training data from master games: for every quiet master decision, each legal move is played and the
// forced captures that follow are resolved (the mover of each capture picks the legal capture that wins most
// material); the features of the resulting quiet position are written with a flag for the master's choice.
static void Policy(string output, string pdnJsonl)
{
    using var w = new StreamWriter(output);
    var buf = new Mv[256]; var cbuf = new Mv[256];
    Span<float> f = stackalloc float[Tuned.Count];
    int decisions = 0, rows = 0, id = 0;
    foreach (var line in File.ReadLines(pdnJsonl))
    {
        using var doc = JsonDocument.Parse(line);
        foreach (var fr in doc.RootElement.GetProperty("frames").EnumerateArray())
        {
            if (fr.GetProperty("ply").GetInt32() < 4) continue;
            var p = Pos.FromBoard(fr.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).ToArray(), fr.GetProperty("turn").GetInt32(), 0);
            if (p.Count <= 6 || MoveGen.HasCapture(p)) continue;
            var mv = fr.GetProperty("move").GetString()!.Split('-', 'x').Select(int.Parse).ToArray();
            var n = MoveGen.Generate(p, buf);
            if (n < 2) continue;
            var master = -1;
            for (var i = 0; i < n; i++) if (buf[i].From == mv[0] - 1 && buf[i].To == mv[^1] - 1) master = i;
            if (master < 0) continue;
            id++; decisions++;
            for (var i = 0; i < n; i++)
            {
                var c = MoveGen.Make(p, buf[i]);
                for (var guard = 0; guard < 12 && MoveGen.HasCapture(c); guard++)
                {
                    var k = MoveGen.Generate(c, cbuf);
                    var best = 0;
                    for (var j = 1; j < k; j++) if (System.Numerics.BitOperations.PopCount(cbuf[j].Captured) > System.Numerics.BitOperations.PopCount(cbuf[best].Captured)) best = j;
                    c = MoveGen.Make(c, cbuf[best]);
                }
                Tuned.Features(c, f);
                var sb = new System.Text.StringBuilder();
                sb.Append(id).Append(',').Append(i == master ? 1 : 0).Append(',').Append(p.Side).Append(',').Append(Tuned.Phase(c));
                for (var j = 0; j < Tuned.Count; j++) sb.Append(',').Append(f[j].ToString(System.Globalization.CultureInfo.InvariantCulture));
                w.WriteLine(sb); rows++;
            }
        }
    }
    Console.WriteLine($"{decisions} decisioni, {rows} righe");
}


// Marks matches left "active" by a runner that was killed (no Dispose ran) as interrupted, through the store API.
static void MarkInterrupted(string reason, string[] ids)
{
    var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
    var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    foreach (var id in ids)
    {
        var path = Path.Combine(store.DirectoryPath, id + ".json");
        var r = JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(path), web)!;
        if (r.Status is "finished" or "interrupted") { Console.WriteLine($"{id}: già {r.Status}"); continue; }
        store.Save(r with { Status = "interrupted", Winner = null, Reason = reason, UpdatedAt = DateTimeOffset.UtcNow });
        Console.WriteLine($"{id}: interrotta ({reason})");
    }
}

record Player(string Id, string Name, string Endpoint);
record Answer(string ProtocolVersion, string MoveId, string? Comment);
