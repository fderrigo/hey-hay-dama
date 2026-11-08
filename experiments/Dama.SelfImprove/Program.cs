using System.Diagnostics;
using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

var minutes = Option("--minutes", 16);
var learningRate = Option("--learning-rate", 0.03);
var temperature = Option("--temperature", 35.0);
var evaluateEvery = (int)Option("--evaluate-every", 20);
var output = args.Contains("--output") ? args[Array.IndexOf(args, "--output") + 1] : "artifacts/self-improvement";
Directory.CreateDirectory(output);
var started = DateTimeOffset.Now;
var deadline = started.AddMinutes(minutes);
var random = new Random(20261106);
var network = new TrainableNetwork(WeightBuilder.Build());
var reference = new NeuralModel(WeightBuilder.Build());
var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
var trajectoryPath = Path.Combine(output, $"trajectories-{started:yyyyMMdd-HHmmss}.jsonl");
using var trajectories = new StreamWriter(trajectoryPath);
var history = new List<object>();
var recent = new List<double>();
var bestScore = double.NegativeInfinity;
var gameNumber = 0;
var timer = Stopwatch.StartNew();

Console.WriteLine($"Training until {deadline:O}; output {Path.GetFullPath(output)}");
while (DateTimeOffset.Now < deadline || gameNumber == 0)
{
    var opponent = (gameNumber % 4) switch { 0 or 3 => "opale", 1 => "material-greedy", _ => "search-depth-3" };
    var result = Play(opponent, gameNumber % 2 == 0 ? 1 : -1, true);
    recent.Add(result.Reward);
    if (recent.Count > 40) recent.RemoveAt(0);
    gameNumber++;
    if (gameNumber % 10 == 0)
        Console.WriteLine($"games={gameNumber} trainScore={recent.Average():F3} elapsed={timer.Elapsed.TotalSeconds:F0}s");

    if (gameNumber % evaluateEvery == 0 || DateTimeOffset.Now >= deadline)
        EvaluateAndCheckpoint();
}

if (history.Count == 0 || history[^1] is not EvaluationRecord e || e.Games != gameNumber)
    EvaluateAndCheckpoint();

var report = new
{
    startedAt = started,
    endedAt = DateTimeOffset.Now,
    trainingGames = gameNumber,
    algorithm = "Outcome-only REINFORCE with a running reward baseline and Adam; no search targets, datasets, pretrained weights, or external supervision.",
    initialWeights = "Opale v1 deterministic hand-built network (not trained)",
    learningRate,
    temperature,
    reward = "win=1, draw/cap=0, loss=-1",
    history,
    trajectories = Path.GetFullPath(trajectoryPath),
    bestWeights = Path.GetFullPath(Path.Combine(output, "klinamite-reinforce-best.json"))
};
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, NeuralModel.JsonOptions));
Console.WriteLine(JsonSerializer.Serialize(report, NeuralModel.JsonOptions));

GameResult Play(string opponent, int side, bool training)
{
    var position = Position.Initial();
    var own = Participant.Ai(training ? "klinamite-reinforce-training" : "klinamite-reinforce-evaluation", training ? "Klinamite training" : "Klinamite evaluation");
    var other = Participant.Ai(opponent == "opale" ? "opale" : opponent == "search-depth-3" ? "opale-search-depth-3" : "reference-" + opponent,
        opponent == "opale" ? "Opale" : opponent == "search-depth-3" ? "Opale ricerca 3" : "Materialista");
    using var tracked = competition.Start("benchmark", side == 1 ? own : other, side == 1 ? other : own);
    var repetitions = new Dictionary<string, int> { [position.Key] = 1 };
    var samples = new List<DecisionSample>();
    var frames = new List<object>();
    Outcome? outcome = null;
    while (position.Ply < 240)
    {
        var legal = Rules.LegalMoves(position);
        outcome = Rules.GetOutcome(position, repetitions.GetValueOrDefault(position.Key), legal);
        if (outcome is not null) break;
        Move move;
        if (training && position.Turn == side)
        {
            var sample = network.Sample(position, legal, temperature, random);
            samples.Add(sample);
            move = sample.Move;
        }
        else if (position.Turn == side)
            move = new NeuralModel(network.Export()).Choose(position, legal).Move;
        else if (position.Ply < 6)
            move = legal[random.Next(legal.Count)];
        else if (opponent == "opale")
            move = reference.Choose(position, legal).Move;
        else if (opponent == "material-greedy")
            move = legal.OrderByDescending(candidate => Rules.Apply(position, candidate).Board.Sum(piece =>
                Math.Sign(piece) * (Math.Abs(piece) == 2 ? 310 : piece == 0 ? 0 : 100)) * position.Turn).First();
        else
            move = new SearchModel(reference).Choose(position, legal, 3, 30000).Move;
        position = Rules.Apply(position, move);
        repetitions[position.Key] = repetitions.GetValueOrDefault(position.Key) + 1;
        frames.Add(new { ply = position.Ply, move = move.Id, board = position.Board, turn = position.Turn == 1 ? "white" : "black" });
    }
    outcome ??= Rules.GetOutcome(position, repetitions.GetValueOrDefault(position.Key));
    var reward = outcome is null || outcome.Winner == 0 ? 0.0 : outcome.Winner == side ? 1.0 : -1.0;
    tracked.Complete(outcome?.Winner ?? 0, position.Ply, outcome?.Reason ?? "benchmark-ply-limit");
    if (training) network.Learn(samples, reward, learningRate);
    trajectories.WriteLine(JsonSerializer.Serialize(new
    {
        matchId = tracked.Id, game = gameNumber + 1, kind = training ? "train" : "evaluation", opponent, side,
        reward, winner = outcome?.Winner ?? 0, reason = outcome?.Reason ?? "benchmark-ply-limit", frames
    }));
    trajectories.Flush();
    return new GameResult(reward);
}

void EvaluateAndCheckpoint()
{
    var wins = 0;
    var draws = 0;
    var losses = 0;
    var byOpponent = new List<object>();
    foreach (var opponent in new[] { "opale", "material-greedy", "search-depth-3" })
    {
        var opponentWins = 0;
        var opponentDraws = 0;
        var opponentLosses = 0;
        for (var game = 0; game < 2; game++)
        {
            var reward = Play(opponent, game == 0 ? 1 : -1, false).Reward;
            if (reward > 0) { wins++; opponentWins++; }
            else if (reward < 0) { losses++; opponentLosses++; }
            else { draws++; opponentDraws++; }
        }
        byOpponent.Add(new { opponent, wins = opponentWins, draws = opponentDraws, losses = opponentLosses });
    }
    var score = (wins + draws * 0.5) / 6.0;
    var record = new EvaluationRecord(gameNumber, wins, draws, losses, Math.Round(score, 4), byOpponent.ToArray());
    history.Add(record);
    var current = network.Export();
    File.WriteAllText(Path.Combine(output, "klinamite-reinforce-current.json"), JsonSerializer.Serialize(current, NeuralModel.JsonOptions));
    if (score >= bestScore)
    {
        bestScore = score;
        File.WriteAllText(Path.Combine(output, "klinamite-reinforce-best.json"), JsonSerializer.Serialize(current, NeuralModel.JsonOptions));
    }
    File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(history, NeuralModel.JsonOptions));
    Console.WriteLine($"CHECKPOINT games={gameNumber} W/D/L={wins}/{draws}/{losses} score={score:F3} best={bestScore:F3}");
}

double Option(string name, double fallback)
{
    var index = Array.IndexOf(args, name);
    return index < 0 ? fallback : double.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
}

sealed record GameResult(double Reward);
sealed record EvaluationRecord(int Games, int Wins, int Draws, int Losses, double Score, object[] Opponents);
sealed record Candidate(Move Move, Position Position, int Perspective, double[] Activations, double Probability);
sealed record DecisionSample(Move Move, Candidate[] Candidates, int Chosen);

sealed class TrainableNetwork
{
    private readonly string[] features;
    private readonly int[][] inputs;
    private readonly int[] bias;
    private readonly int[] output;
    private readonly int[] connection;
    private readonly double[] parameters;
    private readonly double[] gradient;
    private readonly double[] firstMoment;
    private readonly double[] secondMoment;
    private readonly double[] activations;
    private int step;
    private double baseline;

    public TrainableNetwork(NetworkWeights weights)
    {
        features = weights.Hidden.Select(neuron => neuron.Feature).ToArray();
        inputs = weights.Hidden.Select(neuron => neuron.Connections.Select(link => link.Input).ToArray()).ToArray();
        bias = new int[weights.Hidden.Length];
        output = new int[weights.Hidden.Length];
        connection = new int[weights.Hidden.Length];
        var count = weights.Hidden.Sum(neuron => neuron.Connections.Length + 2);
        parameters = new double[count];
        gradient = new double[count];
        firstMoment = new double[count];
        secondMoment = new double[count];
        activations = new double[weights.Hidden.Length];
        var index = 0;
        for (var h = 0; h < weights.Hidden.Length; h++)
        {
            bias[h] = index;
            parameters[index++] = weights.Hidden[h].Bias;
            output[h] = index;
            parameters[index++] = weights.Hidden[h].OutputWeight;
            connection[h] = index;
            foreach (var link in weights.Hidden[h].Connections) parameters[index++] = link.Weight;
        }
    }

    public NetworkWeights Export()
    {
        var hidden = new Neuron[features.Length];
        for (var h = 0; h < hidden.Length; h++)
            hidden[h] = new Neuron(features[h], parameters[bias[h]], parameters[output[h]],
                inputs[h].Select((input, i) => new Connection(input, parameters[connection[h] + i])).ToArray());
        return new NetworkWeights("dama-relu/v1", "Klinamite (outcome-only REINFORCE)", 128,
            "Updated only from states, legal moves and terminal rewards accumulated through registered play.", hidden);
    }

    public DecisionSample Sample(Position position, IReadOnlyList<Move> legal, double temperature, Random random)
    {
        var candidates = new Candidate[legal.Count];
        var scores = new double[legal.Count];
        var maximum = double.NegativeInfinity;
        for (var i = 0; i < legal.Count; i++)
        {
            var next = Rules.Apply(position, legal[i]);
            var outcome = Rules.GetOutcome(next);
            var activations = new double[features.Length];
            var value = outcome is { Winner: 0 } ? 0 : outcome is not null ? 100_000 : Evaluate(next, position.Turn, activations);
            candidates[i] = new Candidate(legal[i], next, position.Turn, activations, 0);
            scores[i] = value;
            maximum = Math.Max(maximum, value);
        }
        var total = 0.0;
        for (var i = 0; i < scores.Length; i++)
        {
            scores[i] = Math.Exp(Math.Clamp((scores[i] - maximum) / temperature, -60, 0));
            total += scores[i];
        }
        var draw = random.NextDouble() * total;
        var chosen = scores.Length - 1;
        for (var i = 0; i < scores.Length; i++)
        {
            scores[i] /= total;
            candidates[i] = candidates[i] with { Probability = scores[i] };
            draw -= scores[i] * total;
            if (draw <= 0 && i < chosen) { chosen = i; break; }
        }
        return new DecisionSample(candidates[chosen].Move, candidates, chosen);
    }

    public void Learn(IReadOnlyList<DecisionSample> samples, double reward, double learningRate)
    {
        if (samples.Count == 0) return;
        Array.Clear(gradient);
        var advantage = reward - baseline;
        baseline += 0.05 * (reward - baseline);
        foreach (var sample in samples)
        foreach (var candidate in sample.Candidates)
        {
            var selected = ReferenceEquals(candidate, sample.Candidates[sample.Chosen]);
            var coefficient = advantage / 35.0 * ((selected ? 1.0 : 0.0) - candidate.Probability);
            for (var h = 0; h < features.Length; h++)
            {
                var activation = candidate.Activations[h];
                gradient[output[h]] += coefficient * activation;
                if (activation <= 0) continue;
                var hiddenGradient = coefficient * parameters[output[h]];
                gradient[bias[h]] += hiddenGradient;
                var input = Encode(candidate.Position, candidate.Perspective);
                for (var i = 0; i < inputs[h].Length; i++)
                    gradient[connection[h] + i] += hiddenGradient * input[inputs[h][i]];
            }
        }
        step++;
        var correction1 = 1 - Math.Pow(0.9, step);
        var correction2 = 1 - Math.Pow(0.999, step);
        for (var i = 0; i < parameters.Length; i++)
        {
            var g = gradient[i] / samples.Count;
            firstMoment[i] = 0.9 * firstMoment[i] + 0.1 * g;
            secondMoment[i] = 0.999 * secondMoment[i] + 0.001 * g * g;
            parameters[i] += learningRate * (firstMoment[i] / correction1) / (Math.Sqrt(secondMoment[i] / correction2) + 1e-8);
        }
    }

    private double Evaluate(Position position, int perspective, double[] destination)
    {
        var input = Encode(position, perspective);
        var value = 0.0;
        for (var h = 0; h < features.Length; h++)
        {
            var sum = parameters[bias[h]];
            for (var i = 0; i < inputs[h].Length; i++) sum += input[inputs[h][i]] * parameters[connection[h] + i];
            destination[h] = Math.Max(0, sum);
            value += destination[h] * parameters[output[h]];
        }
        return value;
    }

    private static double[] Encode(Position position, int perspective)
    {
        var input = new double[128];
        for (var square = 0; square < 32; square++)
        {
            var piece = position.Board[square] * perspective;
            if (piece == 0) continue;
            var normalized = perspective == 1 ? square : 31 - square;
            var plane = piece switch { 1 => 0, 2 => 1, -1 => 2, _ => 3 };
            input[plane * 32 + normalized] = 1;
        }
        return input;
    }
}
