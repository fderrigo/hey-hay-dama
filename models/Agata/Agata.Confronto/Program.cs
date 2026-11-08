// Measurement only: frozen weights, paired colours, registered games. Nothing here changes a weight.
// usage: Agata.Confronto <weights.json> <id> <name> <temperature> <salt> [pairs]
using System.Globalization;
using System.Text.Json;
using Agata;
using Dama.Competition;
using Dama.Engine;
using Network = Agata.Network;

var (path, id, name) = (args[0], args[1], args[2]);
var temperature = double.Parse(args[3], CultureInfo.InvariantCulture);
var salt = int.Parse(args[4]);
var pairs = args.Length > 5 ? int.Parse(args[5]) : 49;
var root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "WeightSpaceGames.slnx"))) root = Path.GetDirectoryName(root)!;
var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
var agata = Network.Load(Path.Combine(root, "models", "Agata", "Agata", "Models", "agata-v1.json"));
var candidate = Network.Load(path);
var opale = NeuralModel.Load(Path.Combine(root, "src", "Dama.Web", "Models", "opale-v1.json"));
var klinamite = NeuralModel.Load(Path.Combine(root, "artifacts", "klinamite-winrate", "klinamite-v1.json"));
var klinamiteV2 = Klinamite.Network.Load(Path.Combine(root, "models", "Klinamite", "Models", "klinamite-v3.json"));
var opaleC = new Opale.C.Model(Opale.C.Network.Load(Path.Combine(root, "models", "Opale.C", "Models", "opale-c-v2.json")));
var kimberlite = Dama.Neural.NeuralNetwork.Load(Path.Combine(root, "src", "Dama.Neural", "Models", "kimberlite-v1.json"));

Move Net(Network net, double t, Position p, List<Move> legal, Random r) =>
    legal[Policy.Choose(Policy.Values(net, p.Board, p.Turn, legal.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray()), t, r)];

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

var me = new Player(Participant.Ai(id, name), (p, l, r) => Net(candidate, temperature, p, l, r));
Player[] rivals =
[
    new(Participant.Ai("agata", "Agata"), (p, l, r) => Net(agata, 10, p, l, r)),
    new(Participant.Ai("opale", "Opale"), (p, l, _) => opale.Choose(p, l).Move),
    new(Participant.Ai("kimberlite", "Kimberlite"), (p, l, _) => Kimberlite(p, l)),
    new(Participant.Ai("klinamite", "Klinamite"), (p, l, _) => klinamite.Choose(p, l).Move),
    new(Participant.Ai("klinamite-v2", "Klinamite v2"), (p, l, _) => l[Klinamite.Policy.Choose(Klinamite.Policy.Values(klinamiteV2, p.Board, p.Turn,
        l.Select(m => new Klinamite.Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray()))]),
    new(Participant.Ai("opale-c", "Opale C"), (p, l, _) => opaleC.Choose(p, l).Move),
];

var draws = new Dictionary<string, int>();

int Game(Player white, Player black, int seed)
{
    for (var attempt = 0; ; attempt++)
        try
        {
            var r = new Random(seed);
            using var tracked = competition.Start("benchmark", white.Who, black.Who);
            var p = Position.Initial();
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            Outcome? outcome = null;
            while (p.Ply < 240)
            {
                var legal = Rules.LegalMoves(p);
                outcome = Rules.GetOutcome(p, seen.GetValueOrDefault(p.Key), legal);
                if (outcome is not null) break;
                var move = p.Ply < 4 ? legal[r.Next(legal.Count)] : (p.Turn == 1 ? white : black).Move(p, legal, r);
                p = Rules.Apply(p, move);
                seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
            }
            tracked.Complete(outcome?.Winner ?? 0, p.Ply, outcome?.Reason ?? "ply-limit");
            if ((outcome?.Winner ?? 0) == 0)
            {
                // Diagnostic only: who stood better, in men, when the game was drawn. No weight reads this.
                var side = white == me ? 1 : -1;
                var balance = p.Board.Sum(x => x == 0 ? 0 : Math.Sign(x) * (Math.Abs(x) == 2 ? 2 : 1)) * side;
                var bucket = (outcome?.Reason ?? "ply-limit") + (balance > 0 ? ":ahead" : balance < 0 ? ":behind" : ":even")
                    + ":men" + p.Board.Count(x => Math.Abs(x) == 1) + ":kings" + p.Board.Count(x => Math.Abs(x) == 2);
                lock (draws) draws[bucket] = draws.GetValueOrDefault(bucket) + 1;
            }
            return outcome?.Winner ?? 0;
        }
        catch (IOException) when (attempt < 12) { }
}

var only = args.Length > 6 ? args[6].Split(',') : null;
foreach (var rival in rivals.Where(r => only is null || only.Contains(r.Who.Id[3..])))
{
    int w = 0, d = 0, l = 0;
    lock (draws) draws.Clear();
    Parallel.For(0, pairs * 2, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
    {
        var white = i % 2 == 0;
        var winner = white ? Game(me, rival, salt * 7919 + i / 2) : Game(rival, me, salt * 7919 + i / 2);
        if (winner == 0) Interlocked.Increment(ref d);
        else if ((winner == 1) == white) Interlocked.Increment(ref w);
        else Interlocked.Increment(ref l);
    });
    Console.WriteLine(JsonSerializer.Serialize(new { me = id, temperature, salt, rival = rival.Who.Id, wins = w, draws = d, losses = l, score = Math.Round((w + d / 2.0) / (w + d + l), 3) }));
    if (only is not null) foreach (var (key, count) in draws.OrderByDescending(x => x.Value).Take(12)) Console.WriteLine($"# {key} {count}");
}

internal sealed record Player(Participant Who, Func<Position, List<Move>, Random, Move> Move);
