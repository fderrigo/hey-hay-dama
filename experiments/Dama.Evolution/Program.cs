using System.Diagnostics;
using System.Text.Json;
using Dama.Competition;
using Dama.Engine;
using Dama.Neural;
using EngineWeights = Dama.Engine.NetworkWeights;

var minutes = Option("--minutes", 10);
var seed = (int)Option("--seed", 20261306);
var output = args.Contains("--output") ? args[Array.IndexOf(args, "--output") + 1] : "artifacts/evolution";
Directory.CreateDirectory(output);
var started = DateTimeOffset.Now;
var deadline = started.AddMinutes(minutes);
var random = new Random(seed);
var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
var reference = new NeuralModel(WeightBuilder.Build());
var kimberlite = new KimberliteModel();
var opponents = new[] { "opale", "kimberlite", "material-greedy", "search-depth-3" };
var bestWeights = WeightBuilder.Build();
var trajectoryPath = Path.Combine(output, $"trajectories-{started:yyyyMMdd-HHmmss}.jsonl");
using var trajectories = new StreamWriter(trajectoryPath);
var bestFitness = Fitness(bestWeights, 0);
var sigma = 1.0;
var candidateNumber = 0;
var improvements = 0;
var stale = 0;
var history = new List<object> { new { candidate = 0, fitness = bestFitness, sigma } };
var timer = Stopwatch.StartNew();
SaveBest();
Console.WriteLine($"Initial fitness={bestFitness:F4}; deadline={deadline:O}");

while (DateTimeOffset.Now < deadline)
{
    candidateNumber++;
    var candidate = Mutate(bestWeights, sigma);
    var fitness = Fitness(candidate, candidateNumber);
    if (fitness > bestFitness)
    {
        bestWeights = candidate;
        bestFitness = fitness;
        improvements++;
        stale = 0;
        sigma = Math.Min(2.0, sigma * 1.1);
        SaveBest();
        Console.WriteLine($"IMPROVED candidate={candidateNumber} fitness={bestFitness:F4} sigma={sigma:F3}");
    }
    else
    {
        stale++;
        if (stale % 75 == 0) sigma = Math.Max(0.1, sigma * 0.75);
    }
    if (candidateNumber % 25 == 0)
    {
        history.Add(new { candidate = candidateNumber, fitness = bestFitness, sigma });
        File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(history, NeuralModel.JsonOptions));
        Console.WriteLine($"candidates={candidateNumber} fitness={bestFitness:F4} improvements={improvements} elapsed={timer.Elapsed.TotalSeconds:F0}s");
    }
}

File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    startedAt = started,
    endedAt = DateTimeOffset.Now,
    candidatesEvaluated = candidateNumber,
    improvements,
    bestFitness,
    sigma,
    algorithm = "Outcome-only paired-colour evolution of direct neural weights; no datasets, pretrained weights, labels, search targets, or external supervision.",
    trainingSeed = seed,
    opponents,
    trajectories = Path.GetFullPath(trajectoryPath),
    bestWeights = Path.GetFullPath(Path.Combine(output, "klinamite-v1.json"))
}, NeuralModel.JsonOptions));
Console.WriteLine($"DONE candidates={candidateNumber} improvements={improvements} fitness={bestFitness:F4}");

EngineWeights Mutate(EngineWeights source, double scale)
{
    var hidden = source.Hidden.ToArray();
    var half = hidden.Length / 2;
    for (var mutation = 0; mutation < 16; mutation++)
    {
        var index = random.Next(half);
        var delta = Gaussian() * scale;
        foreach (var h in new[] { index, index + half })
        {
            var old = hidden[h];
            var magnitude = Math.Max(1.0, Math.Abs(old.OutputWeight) * 0.05);
            var signedDelta = delta * magnitude * Math.Sign(old.OutputWeight);
            hidden[h] = old with { OutputWeight = old.OutputWeight + signedDelta };
        }
    }
    return source with { Name = "Klinamite v1 (outcome-only evolution)", Construction = "Weights selected and modified only through registered play and terminal outcomes.", Hidden = hidden };
}

double Gaussian() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

double Fitness(EngineWeights weights, int candidate)
{
    var model = new NeuralModel(weights);
    var score = 0.0;
    foreach (var opponent in opponents)
    for (var game = 0; game < 2; game++)
        score += Play(model, opponent, game == 0 ? 1 : -1, candidate);
    return score / 8.0;
}

double Play(NeuralModel model, string opponent, int side, int candidate)
{
    var position = Position.Initial();
    var openingRandom = new Random(seed + opponent switch { "opale" => 0, "kimberlite" => 17, "material-greedy" => 34, _ => 51 });
    var own = Participant.Ai("klinamite-evolution", "Klinamite evolution");
    var other = Participant.Ai(opponent switch { "opale" => "opale", "kimberlite" => "kimberlite", "search-depth-3" => "opale-search-depth-3", _ => "reference-" + opponent },
        opponent switch { "opale" => "Opale", "kimberlite" => "Kimberlite", "search-depth-3" => "Opale ricerca 3", _ => "Materialista" });
    using var tracked = competition.Start("benchmark", side == 1 ? own : other, side == 1 ? other : own);
    var repetitions = new Dictionary<string, int> { [position.Key] = 1 };
    var frames = new List<object>();
    Outcome? outcome = null;
    while (position.Ply < 240)
    {
        var legal = Rules.LegalMoves(position);
        outcome = Rules.GetOutcome(position, repetitions.GetValueOrDefault(position.Key), legal);
        if (outcome is not null) break;
        Move move;
        if (position.Ply < 6) move = legal[openingRandom.Next(legal.Count)];
        else if (position.Turn == side) move = model.Choose(position, legal).Move;
        else if (opponent == "opale") move = reference.Choose(position, legal).Move;
        else if (opponent == "kimberlite") move = legal.OrderByDescending(candidateMove =>
        {
            var next = Rules.Apply(position, candidateMove);
            var terminal = Rules.GetOutcome(next);
            return terminal is { Winner: 0 } ? 0 : terminal is not null ? 100_000 : kimberlite.Network.Evaluate(next, position.Turn);
        }).First();
        else if (opponent == "material-greedy") move = legal.OrderByDescending(candidateMove => Rules.Apply(position, candidateMove).Board.Sum(piece =>
            Math.Sign(piece) * (Math.Abs(piece) == 2 ? 310 : piece == 0 ? 0 : 100)) * position.Turn).First();
        else move = new SearchModel(reference).Choose(position, legal, 3, 30000).Move;
        position = Rules.Apply(position, move);
        repetitions[position.Key] = repetitions.GetValueOrDefault(position.Key) + 1;
        frames.Add(new { ply = position.Ply, move = move.Id, board = position.Board, turn = position.Turn == 1 ? "white" : "black" });
    }
    outcome ??= Rules.GetOutcome(position, repetitions.GetValueOrDefault(position.Key));
    tracked.Complete(outcome?.Winner ?? 0, position.Ply, outcome?.Reason ?? "benchmark-ply-limit");
    var reward = outcome is not null && outcome.Winner == side ? 1.0 : 0.0;
    trajectories.WriteLine(JsonSerializer.Serialize(new { matchId = tracked.Id, candidate, opponent, side, reward, frames }));
    return reward;
}

void SaveBest() => File.WriteAllText(Path.Combine(output, "klinamite-v1.json"), JsonSerializer.Serialize(bestWeights, NeuralModel.JsonOptions));

double Option(string name, double fallback)
{
    var index = Array.IndexOf(args, name);
    return index < 0 ? fallback : double.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
}
