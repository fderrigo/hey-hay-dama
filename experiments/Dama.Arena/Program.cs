using Dama.Competition;
using System.Diagnostics;
using System.Text.Json;
using Dama.Engine;
using Dama.Neural;

var weightsPath = args.Contains("--weights") ? args[Array.IndexOf(args, "--weights") + 1] : null;
var model = weightsPath is null ? new NeuralModel(WeightBuilder.Build()) : NeuralModel.Load(weightsPath);
if (args.Length >= 2 && args[0] == "--export")
{
    var path = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(model.Weights, NeuralModel.JsonOptions));
    Console.WriteLine($"Exported {model.Weights.Hidden.Length} ReLU units, {model.ParameterCount} stored parameters to {path}");
    return;
}
var gamesPerOpponent = args.Contains("--games") ? int.Parse(args[Array.IndexOf(args, "--games") + 1]) : 12;
if (gamesPerOpponent < 2 || gamesPerOpponent > 200 || gamesPerOpponent % 2 != 0) throw new ArgumentException("--games must be even, between 2 and 200.");
var output = args.Contains("--output") ? args[Array.IndexOf(args, "--output") + 1] : "experiments/benchmark.json";
var seed = args.Contains("--seed") ? int.Parse(args[Array.IndexOf(args, "--seed") + 1]) : 20261106;
var opponents = args.Contains("--opponents") ? args[Array.IndexOf(args, "--opponents") + 1].Split(',') : ["random", "material-greedy", "search-depth-3"];
var reference = new NeuralModel(WeightBuilder.Build());
var kimberlite = new KimberliteModel();
var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
var timer = Stopwatch.StartNew();
var results = new List<object>();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
using var trajectories = new StreamWriter(Path.ChangeExtension(Path.GetFullPath(output), ".trajectories.jsonl"));
foreach (var opponent in opponents)
{
    var wins = 0; var losses = 0; var draws = 0; var capped = 0; var plies = 0;
    for (var game = 0; game < gamesPerOpponent; game++)
    {
        var random = new Random(seed + game / 2);
        var p = Position.Initial();
        var side = game % 2 == 0 ? 1 : -1;
        var ownPlayer = Participant.Ai(weightsPath is null ? "opale" : "klinamite", model.Weights.Name);
        var otherPlayer = Participant.Ai(opponent switch { "search-depth-3" => "opale-search-depth-3", "opale" => "opale", "kimberlite" => "kimberlite", _ => "reference-" + opponent },
            opponent switch { "search-depth-3" => "Opale ricerca 3", "opale" => "Opale", "kimberlite" => "Kimberlite", "random" => "Casuale", _ => "Materialista" });
        using var tracked = competition.Start("benchmark", side == 1 ? ownPlayer : otherPlayer, side == 1 ? otherPlayer : ownPlayer);
        var repetitions = new Dictionary<string, int> { [p.Key] = 1 };
        var frames = new List<object>();
        Outcome? outcome = null;
        while (p.Ply < 240)
        {
            var legal = Rules.LegalMoves(p);
            outcome = Rules.GetOutcome(p, repetitions.GetValueOrDefault(p.Key), legal);
            if (outcome is not null) break;
            Move move;
            if (p.Ply < 6) move = legal[random.Next(legal.Count)];
            else if (p.Turn == side) move = model.Choose(p, legal).Move;
            else if (opponent == "random") move = legal[random.Next(legal.Count)];
            else if (opponent == "opale") move = reference.Choose(p, legal).Move;
            else if (opponent == "kimberlite")
                move = legal.OrderByDescending(candidate =>
                {
                    var next = Rules.Apply(p, candidate);
                    var terminal = Rules.GetOutcome(next);
                    return terminal is { Winner: 0 } ? 0 : terminal is not null ? 100_000 : kimberlite.Network.Evaluate(next, p.Turn);
                }).First();
            else if (opponent == "material-greedy")
                move = legal.OrderByDescending(m => Rules.Apply(p, m).Board.Sum(piece => Math.Sign(piece) * (Math.Abs(piece) == 2 ? 310 : piece == 0 ? 0 : 100)) * p.Turn).First();
            else move = new SearchModel(reference).Choose(p, legal, 3, 30000).Move;
            p = Rules.Apply(p, move);
            repetitions[p.Key] = repetitions.GetValueOrDefault(p.Key) + 1;
            frames.Add(new { ply = p.Ply, move = move.Id, board = p.Board, turn = p.Turn == 1 ? "white" : "black" });
        }
        outcome ??= Rules.GetOutcome(p, repetitions.GetValueOrDefault(p.Key));
        if (outcome is null) { capped++; draws++; }
        else if (outcome.Winner == 0) draws++;
        else if (outcome.Winner == side) wins++;
        else losses++;
        tracked.Complete(outcome?.Winner ?? 0, p.Ply, outcome?.Reason ?? "benchmark-ply-limit");
        trajectories.WriteLine(JsonSerializer.Serialize(new { matchId = tracked.Id, opponent, side, frames }));
        plies += p.Ply;
    }
    var result = new { opponent, games = gamesPerOpponent, wins, draws, losses, cappedAt240Plies = capped, averagePlies = Math.Round((double)plies / gamesPerOpponent, 1) };
    results.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
}
var report = new
{
    model = model.Weights.Name, createdAtUtc = DateTimeOffset.UtcNow, seed,
    protocol = "Paired colours; six-ply seeded random openings; 240-ply cap counted separately; search baseline depth 3, 30s safeguard per move.",
    hiddenUnits = model.Weights.Hidden.Length, storedParameters = model.ParameterCount,
    evaluationOnly = true, learnedFromPlay = weightsPath is not null, seconds = Math.Round(timer.Elapsed.TotalSeconds, 2), results,
    trajectories = trajectories.BaseStream is FileStream stream ? stream.Name : null,
    limitation = "Small deterministic benchmark against synthetic opponents. Not an Elo estimate or evidence of tournament strength."
};
File.WriteAllText(output, JsonSerializer.Serialize(report, NeuralModel.JsonOptions));
Console.WriteLine($"Report: {Path.GetFullPath(output)}");
