using Dama.Competition;
using System.Diagnostics;
using System.Text.Json;
using Dama.Engine;
using Dama.Neural;

if (args.Length == 2 && args[0] == "--export")
{
    var path = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var weights = KimberliteWeightBuilder.Build();
    File.WriteAllText(path, JsonSerializer.Serialize(weights, NeuralNetwork.JsonOptions));
    Console.WriteLine($"Exported {weights.Hidden.Length} ReLU units, {new NeuralNetwork(weights).ParameterCount} stored parameters to {path}");
    return;
}

var gamesPerOpponent = ReadOption("--games", 12);
if (gamesPerOpponent < 2 || gamesPerOpponent > 100 || gamesPerOpponent % 2 != 0) throw new ArgumentException("--games must be even, between 2 and 100.");
var output = args.Contains("--output") ? args[Array.IndexOf(args, "--output") + 1] : "experiments/kimberlite-benchmark.json";
var model = new KimberliteModel();
var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
var timer = Stopwatch.StartNew();
var results = new List<object>();

foreach (var opponent in new[] { "random", "material-greedy", "ambra-network", "ambra-search-depth-3" })
{
    var wins = 0;
    var losses = 0;
    var draws = 0;
    var capped = 0;
    var plies = 0;
    var decisions = 0;
    var evaluatedPositions = 0;
    for (var game = 0; game < gamesPerOpponent; game++)
    {
        var random = new Random(20261108 + game / 2);
        var position = Position.Initial();
        var modelSide = game % 2 == 0 ? 1 : -1;
        var ownPlayer = Participant.Ai("kimberlite", "Kimberlite");
        var otherPlayer = opponent switch { "ambra-network" => Participant.Ai("opale", "Opale"), "ambra-search-depth-3" => Participant.Ai("opale-search-depth-3", "Opale ricerca 3"), _ => Participant.Ai("reference-" + opponent, opponent == "random" ? "Casuale" : "Materialista") };
        using var tracked = competition.Start("benchmark", modelSide == 1 ? ownPlayer : otherPlayer, modelSide == 1 ? otherPlayer : ownPlayer);
        var repetitions = new Dictionary<string, int> { [position.Key] = 1 };
        Outcome? outcome = null;
        while (position.Ply < 240)
        {
            var legal = Rules.LegalMoves(position);
            outcome = Rules.GetOutcome(position, repetitions.GetValueOrDefault(position.Key), legal);
            if (outcome is not null) break;
            Move move;
            if (position.Ply < 6) move = legal[random.Next(legal.Count)];
            else if (position.Turn == modelSide)
            {
                var player = position.Turn == 1 ? "white" : "black";
                var request = new KimberliteRequest("dama-model/v1", "italian-draughts-8x8", Guid.NewGuid().ToString("N"), tracked.Id, player,
                    new KimberlitePosition(position.Board, player, position.Ply, position.QuietKingPlyCount, repetitions.GetValueOrDefault(position.Key, 1)),
                    legal.Select(ToKimberliteMove).ToArray(), [], 1500);
                var decision = model.Choose(request);
                move = legal.Single(candidate => candidate.Id == decision.MoveId);
                decisions++;
                evaluatedPositions += decision.EvaluatedPositions;
            }
            else if (opponent == "random") move = legal[random.Next(legal.Count)];
            else if (opponent == "material-greedy")
                move = legal.OrderByDescending(candidate => Rules.Apply(position, candidate).Board.Sum(piece => Math.Sign(piece) * (Math.Abs(piece) == 2 ? 300 : piece == 0 ? 0 : 100)) * position.Turn).First();
            else
            {
                var network = new NeuralModel(WeightBuilder.Build());
                move = opponent == "ambra-network" ? network.Choose(position, legal).Move : new SearchModel(network).Choose(position, legal, 3, 30000).Move;
            }
            position = Rules.Apply(position, move);
            repetitions[position.Key] = repetitions.GetValueOrDefault(position.Key) + 1;
        }
        outcome ??= Rules.GetOutcome(position, repetitions.GetValueOrDefault(position.Key));
        if (outcome is null) { capped++; draws++; }
        else if (outcome.Winner == 0) draws++;
        else if (outcome.Winner == modelSide) wins++;
        else losses++;
        tracked.Complete(outcome?.Winner ?? 0, position.Ply, outcome?.Reason ?? "benchmark-ply-limit");
        plies += position.Ply;
    }
    var result = new
    {
        opponent,
        games = gamesPerOpponent,
        wins,
        draws,
        losses,
        cappedAt240Plies = capped,
        averagePlies = Math.Round((double)plies / gamesPerOpponent, 1),
        averageEvaluatedPositions = Math.Round((double)evaluatedPositions / decisions, 1)
    };
    results.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
}

var report = new
{
    model = model.Network.Weights.Name,
    createdAtUtc = DateTimeOffset.UtcNow,
    seed = 20261108,
    protocol = "Paired colours; six-ply seeded random openings; 240-ply cap counted separately; one-ply inference with directly written sparse ReLU weights; no continuation search.",
    inputSize = model.Network.Weights.InputSize,
    hiddenUnits = model.Network.Weights.Hidden.Length,
    storedParameters = model.Network.ParameterCount,
    training = false,
    fineTuning = false,
    continuationSearch = false,
    externalComponents = false,
    seconds = Math.Round(timer.Elapsed.TotalSeconds, 2),
    results,
    limitation = "Small reproducible benchmark against synthetic and built-in opponents. Not an Elo estimate or evidence of tournament strength; one-ply inference misses distant tactics."
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine($"Report: {Path.GetFullPath(output)}");

int ReadOption(string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    return index < 0 ? fallback : int.Parse(args[index + 1]);
}

static KimberliteMove ToKimberliteMove(Move move) => new(move.Id, move.From + 1, move.To + 1,
    move.Path.Select(square => square + 1).ToArray(), move.Captures.Select(square => square + 1).ToArray(), move.Promotes);
