// Agata improves only from registered games: outcomes of play set every target.
// usage: Agata.Sfida --minutes M [--rate gamesPerSecond] [--workers N] [--pairs P]
//        --minutes 0 runs the direct tournament only.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Agata;
using Agata.Sfida;
using Dama.Competition;
using Dama.Engine;
using Network = Agata.Network;

string Opt(string name, string fallback) => Array.IndexOf(args, name) is >= 0 and var i ? args[i + 1] : fallback;
var minutes = double.Parse(Opt("--minutes", "0"), CultureInfo.InvariantCulture);
var rate = double.Parse(Opt("--rate", "30"), CultureInfo.InvariantCulture);
var workers = int.Parse(Opt("--workers", "8"));
var pairs = int.Parse(Opt("--pairs", "49"));
const double Lambda = .7;
const int MaxPlies = 240;
const int AddedNeurons = 96;

var root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "WeightSpaceGames.slnx"))) root = Path.GetDirectoryName(root)!;
var results = Path.Combine(root, "models", "Agata", "results", "sfida");
Directory.CreateDirectory(results);
var v1Path = Path.Combine(root, "models", "Agata", "Agata", "Models", "agata-v1.json");
var v2Path = Path.Combine(results, "agata-v2.json");
var r1Path = Path.Combine(root, "artifacts", "self-improvement", "opale-r1-current.json");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());

var agataV1 = Network.Load(v1Path);
var opale = NeuralModel.Load(Path.Combine(root, "src", "Dama.Web", "Models", "opale-v1.json"));
var opaleR1 = File.Exists(r1Path) ? NeuralModel.Load(r1Path) : null;
var klinamitePath = Path.Combine(root, "artifacts", "klinamite-winrate", "klinamite-v1.json");
var klinamite = File.Exists(klinamitePath) ? NeuralModel.Load(klinamitePath) : null;
var kimberlite = Dama.Neural.NeuralNetwork.Load(Path.Combine(root, "src", "Dama.Neural", "Models", "kimberlite-v1.json"));

Move Agate(Network net, Position p, List<Move> legal, Random r, double temperature) =>
    legal[Policy.Choose(Policy.Values(net, p.Board, p.Turn, legal.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray()), temperature, r)];

Move Kimberlite(Position p, List<Move> legal)
{
    var best = legal[0]; var score = double.NegativeInfinity;
    foreach (var m in legal)
    {
        var next = Rules.Apply(p, m);
        var outcome = Rules.GetOutcome(next, legal: Rules.LegalMoves(next));
        var v = outcome is { Winner: 0 } ? 0 : outcome is not null ? 100_000 : kimberlite.Evaluate(next, p.Turn);
        if (v > score) { score = v; best = m; }
    }
    return best;
}

Player Agatan(string id, string name, Network net, double temperature) => new(Participant.Ai(id, name), (p, l, r) => Agate(net, p, l, r, temperature));
var playerAgata = Agatan("agata", "Agata", agataV1, 10);
var playerOpale = new Player(Participant.Ai("opale", "Opale"), (p, l, _) => opale.Choose(p, l).Move);
var playerKimberlite = new Player(Participant.Ai("kimberlite", "Kimberlite"), (p, l, _) => Kimberlite(p, l));
var playerR1 = opaleR1 is null ? null : new Player(Participant.Ai("opale-r1", "Opale R1"), (p, l, _) => opaleR1.Choose(p, l).Move);
var playerKlinamite = klinamite is null ? null : new Player(Participant.Ai("klinamite", "Klinamite"), (p, l, _) => klinamite.Choose(p, l).Move);

(int Winner, int Plies) Play(Player white, Player black, int openingPlies, Random r, List<Position>? visited)
{
    using var tracked = competition.Start("benchmark", white.Who, black.Who);
    var p = Position.Initial();
    var seen = new Dictionary<string, int> { [p.Key] = 1 };
    Outcome? outcome = null;
    while (p.Ply < MaxPlies)
    {
        visited?.Add(p);
        var legal = Rules.LegalMoves(p);
        outcome = Rules.GetOutcome(p, seen.GetValueOrDefault(p.Key), legal);
        if (outcome is not null) break;
        var move = p.Ply < openingPlies ? legal[r.Next(legal.Count)] : (p.Turn == 1 ? white : black).Move(p, legal, r);
        p = Rules.Apply(p, move);
        seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
    }
    tracked.Complete(outcome?.Winner ?? 0, p.Ply, outcome?.Reason ?? "ply-limit");
    return (outcome?.Winner ?? 0, p.Ply);
}

object Duel(Player a, Player b, int salt)
{
    int w = 0, d = 0, l = 0;
    Parallel.For(0, pairs * 2, new ParallelOptions { MaxDegreeOfParallelism = 6 }, i =>
    {
        var aWhite = i % 2 == 0;
        // Both colours of a pair share one seeded four-ply opening.
        var r = new Random(salt * 7919 + i / 2);
        (int Winner, int Plies) game = default;
        for (var attempt = 0; ; attempt++)
            try { r = new Random(salt * 7919 + i / 2); game = aWhite ? Play(a, b, 4, r, null) : Play(b, a, 4, r, null); break; }
            catch (IOException) when (attempt < 5) { }
        var winner = game.Winner;
        if (winner == 0) Interlocked.Increment(ref d);
        else if ((winner == 1) == aWhite) Interlocked.Increment(ref w);
        else Interlocked.Increment(ref l);
    });
    return new { a = a.Who.Id, b = b.Who.Id, wins = w, draws = d, losses = l, score = Math.Round((w + d / 2.0) / (w + d + l), 3) };
}

using var progress = new StreamWriter(new FileStream(Path.Combine(results, "progress.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
var clock = Stopwatch.StartNew();
void Log(string kind, object data)
{
    var line = JsonSerializer.Serialize(new { kind, at = DateTimeOffset.Now.ToString("HH:mm:ss"), data });
    progress.WriteLine(line); Console.WriteLine(line);
}

if (args.Contains("--measure"))
{
    var measured = Agatan("agata-v2", "Agata v2", Network.Load(v2Path), 10);
    foreach (var rival in new[] { playerAgata, playerOpale, playerKimberlite, playerR1, playerKlinamite }.OfType<Player>())
        Log("measure", Duel(measured, rival, 7000));
    return;
}
if (minutes <= 0)
{
    var field = new[] { playerAgata, playerOpale, playerKimberlite, playerR1, playerKlinamite }.OfType<Player>().ToArray();
    for (var i = 0; i < field.Length; i++)
        for (var j = i + 1; j < field.Length; j++)
            Log("tournament", Duel(field[i], field[j], 1000 + i * 10 + j));
    return;
}

var start = JsonSerializer.Deserialize<NetworkDefinition>(File.ReadAllBytes(File.Exists(v2Path) ? v2Path : v1Path), json)!;
var trainer = new Trainer(start, AddedNeurons, 20261106);
var current = new Network(trainer.Export());
var past = current;
var budget = TimeSpan.FromMinutes(minutes);
long games = 0, states = 0, failures = 0;
var sfida = Participant.Ai("agata-sfida", "Agata sfida");

var threads = Enumerable.Range(0, workers).Select(id => new Thread(() =>
{
    var r = new Random(unchecked(Environment.TickCount * 31 + id));
    var buffer = trainer.Buffer(); var active = new int[32]; var state = new byte[32];
    var memory = new Memory(300_000);
    var path = new List<Position>(); var values = new float[MaxPlies + 2]; var goals = new float[MaxPlies + 2];
    while (clock.Elapsed < budget)
    {
        if (Interlocked.Read(ref games) > rate * clock.Elapsed.TotalSeconds) { Thread.Sleep(5); continue; }
        Interlocked.Increment(ref games);
        var f = clock.Elapsed / budget;
        var step = (float)(.01 * (1 - f) + .003 * f);
        var live = Volatile.Read(ref current);
        var pick = r.NextDouble();
        var rival = pick < .25 ? playerAgata : pick < .45 ? Agatan("agata", "Agata", Volatile.Read(ref past), 10)
            : pick < .65 ? playerKimberlite : pick < .75 ? playerR1 ?? playerOpale : pick < .85 ? playerKlinamite ?? playerOpale : playerOpale;
        var me = new Player(rival.Who.Id == "ai:agata" ? rival.Who : sfida, (p, l, x) => Agate(live, p, l, x, 30));
        var white = r.Next(2) == 0;
        path.Clear();
        int winner;
        try { (winner, _) = white ? Play(me, rival, 4, r, path) : Play(rival, me, 4, r, path); }
        catch (IOException) { Interlocked.Increment(ref failures); continue; }
        // λ-returns for the side to move. The last position carries only the game result.
        var end = path.Count - 1;
        goals[end] = winner * path[end].Turn;
        for (var t = 0; t <= end; t++) values[t] = (float)Math.Tanh(live.Evaluate(path[t].Board, path[t].Turn) / Trainer.Scale);
        for (var t = end - 1; t >= 0; t--)
            goals[t] = -(float)((1 - Lambda) * (t + 1 == end ? goals[end] : values[t + 1]) + Lambda * goals[t + 1]);
        for (var t = 0; t <= end; t++)
        {
            Trainer.Encode(path[t].Board, path[t].Turn, active);
            memory.Add(active, goals[t]);
            for (var k = 0; k < 32; k++) state[k] = (byte)active[k];
            trainer.Update(state, goals[t], step, buffer);
        }
        memory.Replay(trainer, 300, step, buffer, r);
        Interlocked.Add(ref states, path.Count);
    }
}) { IsBackground = true }).ToArray();
foreach (var t in threads) t.Start();

var refresh = 0;
while (clock.Elapsed < budget)
{
    Thread.Sleep(2000);
    Volatile.Write(ref current, new Network(trainer.Export()));
    if (++refresh % 30 == 0) Volatile.Write(ref past, Volatile.Read(ref current));
}
foreach (var t in threads) t.Join();
var exported = trainer.Export();
var candidate = Path.Combine(results, "agata-v2.candidate.json");
File.WriteAllText(candidate, JsonSerializer.Serialize(exported with { Name = "Agata v2", Construction = start.Construction.Split(" | ")[0] + " | Readout and added neurons updated only from registered play." }, json));
File.Move(candidate, v2Path, true);
Log("cycle", new { seconds = (int)clock.Elapsed.TotalSeconds, games, states, failures });

// The saved file is the one measured, through the same class the service uses.
var saved = Network.Load(v2Path);
var v2 = Agatan("agata-v2", "Agata v2", saved, 10);
foreach (var rival in new[] { playerAgata, playerOpale, playerKimberlite, playerR1, playerKlinamite }.OfType<Player>())
    Log("measure", Duel(v2, rival, 5000 + (int)DateTime.Now.TimeOfDay.TotalMinutes));

internal sealed record Player(Participant Who, Func<Position, List<Move>, Random, Move> Move);
