using Dama.Competition;
// Reproducible evaluation of the network alone. Dama.Engine is the referee.
// usage: Agata.Eval <weights.json> <output.json> [--seed N]
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dama.Engine;
using Agata;
using Candidate = Agata.Candidate;
using AgataNetwork = Agata.Network;

var network = AgataNetwork.Load(args[0]);
var output = args[1];
var seedAt = Array.IndexOf(args, "--seed");
var seed = seedAt >= 0 ? int.Parse(args[seedAt + 1]) : 2026;
var root = FindRepositoryRoot();
var ambra = NeuralModel.Load(Path.Combine(root, "src", "Dama.Web", "Models", "opale-v1.json"));
const double Temperature = 10;

Move Ours(Position p, List<Move> legal, Random random)
{
    var candidates = legal.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
    return legal[Policy.Choose(Policy.Values(network, p.Board, p.Turn, candidates), Temperature, random)];
}

(string Name, Func<Position, List<Move>, Random, Move> Opponent)[] suites =
[
    ("casuale", (_, legal, random) => legal[random.Next(legal.Count)]),
    ("ambra-rete", (p, legal, _) => ambra.Choose(p, legal).Move),
    ("ambra-ricerca-d6-1500ms", (p, legal, _) => new SearchModel(ambra).Choose(p, legal, 6, 1500).Move),
];

var openings = new List<Move[]>();
foreach (var first in Rules.LegalMoves(Position.Initial()))
    foreach (var second in Rules.LegalMoves(Rules.Apply(Position.Initial(), first)))
        openings.Add([first, second]);

var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
var report = new List<object>();
foreach (var (name, opponent) in suites)
{
    var work = openings.SelectMany((o, i) => new[] { (o, i, true), (o, i, false) }).ToArray();
    var games = new ConcurrentBag<(int Opening, bool White, double Score, int Plies, string Reason, string Moves)>();
    Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = 6 }, item =>
    {
        var (opening, index, oursWhite) = item;
        var ownPlayer = Participant.Ai("agata", "Agata");
        var otherPlayer = name switch { "ambra-rete" => Participant.Ai("opale", "Opale"), "ambra-ricerca-d6-1500ms" => Participant.Ai("opale-search-depth-6", "Opale ricerca 6"), _ => Participant.Ai("reference-random", "Casuale") };
        using var tracked = competition.Start("benchmark", oursWhite ? ownPlayer : otherPlayer, oursWhite ? otherPlayer : ownPlayer);
        var random = new Random(seed * 1009 + index * 2 + (oursWhite ? 1 : 0));
        var p = Position.Initial();
        var seen = new Dictionary<string, int> { [p.Key] = 1 };
        var moves = new List<string>();
        Outcome? outcome = null;
        var ply = 0;
        while (outcome is null)
        {
            var legal = Rules.LegalMoves(p);
            var move = ply < 2 ? opening[ply] : (p.Turn == 1) == oursWhite ? Ours(p, legal, random) : opponent(p, legal, random);
            if (legal.All(m => m.Id != move.Id)) throw new InvalidOperationException($"Illegal move {move.Id}");
            moves.Add(move.Id);
            p = Rules.Apply(p, move);
            seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
            outcome = Rules.GetOutcome(p, seen[p.Key]);
            ply++;
        }
        tracked.Complete(outcome.Winner, p.Ply, outcome.Reason);
        var score = outcome.Winner == 0 ? 0.5 : (outcome.Winner == 1) == oursWhite ? 1.0 : 0.0;
        games.Add((index, oursWhite, score, p.Ply, outcome.Reason, string.Join(' ', moves)));
    });
    var ordered = games.OrderBy(g => g.Opening).ThenBy(g => g.White).ToArray();
    var mean = ordered.Average(g => g.Score);
    var error = Math.Sqrt(ordered.Sum(g => (g.Score - mean) * (g.Score - mean)) / (ordered.Length - 1) / ordered.Length);
    var result = new
    {
        opponent = name, games = ordered.Length,
        wins = ordered.Count(g => g.Score == 1), draws = ordered.Count(g => g.Score == 0.5), losses = ordered.Count(g => g.Score == 0),
        scoreRate = Math.Round(mean, 4), ci95 = new[] { Math.Round(mean - 1.96 * error, 4), Math.Round(mean + 1.96 * error, 4) },
        whiteScore = Math.Round(ordered.Where(g => g.White).Average(g => g.Score), 4), blackScore = Math.Round(ordered.Where(g => !g.White).Average(g => g.Score), 4),
        averagePlies = Math.Round(ordered.Average(g => g.Plies), 1),
        reasons = ordered.GroupBy(g => g.Reason).ToDictionary(g => g.Key, g => g.Count()),
        transcriptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', ordered.Select(g => g.Moves))))).ToLowerInvariant()[..16]
    };
    report.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
}
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    model = "agata-v1", strategy = "sola rete, nessuna ricerca", temperature = Temperature, seed, openings = openings.Count,
    method = "Ogni apertura di 2 semimosse giocata due volte, a colori invertiti. Dama.Engine arbitra regole e risultato.",
    results = report
}, new JsonSerializerOptions { WriteIndented = true }));
return;

static string FindRepositoryRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "WeightSpaceGames.slnx"))) return d.FullName;
    throw new InvalidOperationException("Repository root not found.");
}
