using System.Security.Cryptography;
using System.Text.Json;
using Dama.Engine;
using Dama.Competition;
using Dama.Learn;

if (args.Length >= 2 && args[0] == "--audit") { Audit.Run(args[1]); return; }
if (args.Length >= 2 && args[0] == "--repair-interrupted") { Audit.RepairInterrupted(args[1]); return; }
if (args.Length >= 3 && args[0] == "--export") { Export.Run(args[1], args[2]); return; }
if (args.Length >= 4 && args[0] == "--probe") { Probe.Run(args[1], args[2], DateTimeOffset.Parse(args[3])); return; }
if (args.Length >= 3 && args[0] == "--evolve") { Evolution.Run(args[1], DateTimeOffset.Parse(args[2]), args.Length > 3 ? args[3] : null); return; }
if (args.Length >= 4 && args[0] == "--win") { Probe.Run(args[1], args[2], DateTimeOffset.Parse(args[3]), "artifacts/opale-learning-20261106/memory.json", 4); return; }

var root = Directory.GetCurrentDirectory();
string Arg(string key, string fallback) => Array.IndexOf(args, key) is var i && i >= 0 ? args[i + 1] : fallback;
var deadline = DateTimeOffset.Parse(Arg("--deadline", "2026-11-06T17:21:44Z"));
var output = Path.GetFullPath(Arg("--output", "artifacts/opale-learning-20261106"));
if (File.Exists(Path.Combine(output, "manifest.json"))) throw new InvalidOperationException("Use a fresh output directory.");
Directory.CreateDirectory(output);
var seed = int.Parse(Arg("--seed", "20261106"));
var random = new Random(seed);
var network = ValueNetwork.RandomStart(random);
network.Save(Path.Combine(output, "initial-weights.json"));
var kimberlite = Dama.Neural.NeuralNetwork.Load(Path.Combine(root, "src/Dama.Neural/Models/kimberlite-v1.json"));
var agata = Agata.Network.Load(Path.Combine(root, "models/Agata/Agata/Models/agata-v1.json"));
var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
string[] opponents = ["kimberlite", "agata"];
var learner = Participant.Ai("opale", "Opale");
var memory = new Dictionary<string, string>();
var replay = new List<Episode>();
var results = new List<Result>();
using var games = new StreamWriter(Path.Combine(output, "games.jsonl"), append: true) { AutoFlush = true };
using var progress = new StreamWriter(Path.Combine(output, "progress.jsonl"), append: true) { AutoFlush = true };
bool stopping = false;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping = true; };
var started = DateTimeOffset.UtcNow;
var manifest = new
{
    started, deadline, seed, learner = learner.Id, competitionDirectory = store.DirectoryPath,
    architecture = "128 occupancy -> 64 ReLU + linear skip -> tanh scalar afterstate value",
    initialization = "Independent pseudorandom weights; zero biases. No pretrained parameters.",
    learning = "TD(lambda=.8), gamma=.995, SGD, completed-game replay from both players' afterstates; terminal winner only, no shaping. Winning own state/action memories indexed by opponent and colour.",
    opponentUse = "Frozen opponents produce moves only; neither their values nor weights are copied into the learner or used as targets.",
    adjudication = "Dama.Engine natural terminal outcomes only. Deadline/2000-ply safety interruption has no reward and no points.",
    evaluation = "Standard initial position, alternating colours. Fixed opponents deterministic. Repeats are not independent evidence of generalization. Final neural-only and new random-opening probes reported separately."
};
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

string MemoryKey(string opponent, int side, Position p) => $"{opponent}:{side}:{p.Key}:{p.QuietKingPlyCount}";

Move Opponent(string id, Position p, List<Move> legal)
{
    if (id == "agata")
    {
        var candidates = legal.Select(m => new Agata.Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
        return legal[Agata.Policy.Choose(Agata.Policy.Values(agata, p.Board, p.Turn, candidates), 0, random)];
    }
    double best = double.NegativeInfinity;
    Move chosen = legal[0];
    foreach (var m in legal)
    {
        var next = Rules.Apply(p, m);
        var terminal = Rules.GetOutcome(next);
        double value = terminal is { Winner: 0 } ? 0 : terminal is not null ? 100000 : kimberlite.Evaluate(next, p.Turn);
        if (value > best) { best = value; chosen = m; }
    }
    return chosen;
}

Result Play(string phase, int index, double epsilon, bool useMemory, int openingPlies = 0)
{
    string opponent = opponents[(index / 2) % opponents.Length];
    int side = index % 2 == 0 ? 1 : -1;
    var other = Participant.Ai(opponent, char.ToUpperInvariant(opponent[0]) + opponent[1..]);
    using var tracked = store.Start("benchmark", side == 1 ? learner : other, side == 1 ? other : learner);
    var p = Position.Initial();
    var seen = new Dictionary<string, int> { [p.Key] = 1 };
    var states = new List<int[]>();
    var opponentStates = new List<int[]>();
    var choices = new List<(string Key, string Move)>();
    // Per-match transcript includes the initial state and the state after every single move.
    using var transcript = new StreamWriter(Path.Combine(output, tracked.Id + ".jsonl")) { AutoFlush = true };
    transcript.WriteLine(JsonSerializer.Serialize(new { kind = "start", matchId = tracked.Id, phase, opponent, side, epsilon, useMemory, openingPlies, board = p.Board, p.Turn, p.Ply, p.QuietKingPlyCount }));
    Outcome? outcome = null;
    int memoryMoves = 0;
    while (!stopping && DateTimeOffset.UtcNow < deadline && p.Ply < 2000)
    {
        if (File.Exists(Path.Combine(output, "STOP"))) { stopping = true; break; }
        var legal = Rules.LegalMoves(p);
        outcome = Rules.GetOutcome(p, seen[p.Key], legal);
        if (outcome is not null) break;
        Move move;
        if (p.Ply < openingPlies) move = legal[random.Next(legal.Count)];
        else if (p.Turn != side) move = Opponent(opponent, p, legal);
        else
        {
            string key = MemoryKey(opponent, side, p);
            if (random.NextDouble() < epsilon) move = legal[random.Next(legal.Count)];
            else if (useMemory && memory.TryGetValue(key, out var remembered) && legal.FirstOrDefault(m => m.Id == remembered) is { } known)
            { move = known; memoryMoves++; }
            else
            {
                float best = float.NegativeInfinity;
                move = legal[0];
                foreach (var candidate in legal)
                {
                    var next = Rules.Apply(p, candidate);
                    // Rule outcomes are game mechanics, never heuristic supervision.
                    var terminal = Rules.GetOutcome(next, seen.GetValueOrDefault(next.Key) + 1);
                    float value = terminal is null ? network.Value(ValueNetwork.Encode(next, side)) : terminal.Winner * side;
                    if (value > best) { best = value; move = candidate; }
                }
            }
            choices.Add((key, move.Id));
        }
        bool ours = p.Turn == side;
        p = Rules.Apply(p, move);
        seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
        if (ours) states.Add(ValueNetwork.Encode(p, side));
        else opponentStates.Add(ValueNetwork.Encode(p, -side));
        transcript.WriteLine(JsonSerializer.Serialize(new { kind = "move", move = move.Id, board = p.Board, p.Turn, p.Ply, p.QuietKingPlyCount }));
    }
    outcome ??= Rules.GetOutcome(p, seen[p.Key]);
    if (outcome is not null) tracked.Complete(outcome.Winner, p.Ply, outcome.Reason);
    else tracked.Interrupt(p.Ply, stopping ? "cancelled" : p.Ply >= 2000 ? "safety-ply-limit" : "challenge-deadline");
    transcript.WriteLine(JsonSerializer.Serialize(new { kind = "end", outcome, p.Ply }));
    int? reward = outcome is null ? null : outcome.Winner * side;
    var result = new Result(tracked.Id, phase, opponent, side, reward, p.Ply, memoryMoves, network.Updates, DateTimeOffset.UtcNow);
    games.WriteLine(JsonSerializer.Serialize(result));
    results.Add(result);
    if (phase == "train" && reward is not null && DateTimeOffset.UtcNow < deadline)
    {
        if (reward == 1)
            // Keep the first successful continuation for each state; no opponent imitation.
            foreach (var (key, move) in choices) memory.TryAdd(key, move);
        var episode = new Episode(states.ToArray(), reward.Value);
        var opposingEpisode = new Episode(opponentStates.ToArray(), -reward.Value);
        replay.Add(episode);
        replay.Add(opposingEpisode);
        if (replay.Count > 2000) replay.RemoveAt(0);
        Learn(episode);
        Learn(opposingEpisode);
        for (int i = 0; i < 3 && DateTimeOffset.UtcNow < deadline; i++) Learn(replay[random.Next(replay.Count)]);
    }
    return result;
}

void Learn(Episode episode)
{
    if (episode.States.Length == 0) return;
    var targets = new float[episode.States.Length];
    float target = episode.Reward;
    for (int i = targets.Length - 1; i >= 0; i--)
    {
        targets[i] = target;
        target = .995f * (.8f * target + .2f * network.Value(episode.States[i]));
    }
    // Targets are frozen before updating; every target traces to actual terminal reward or this learner.
    foreach (int i in Enumerable.Range(0, targets.Length).OrderBy(_ => random.Next()))
        network.Train(episode.States[i], targets[i], .002f);
}

object Summary(IEnumerable<Result> rows) => rows.GroupBy(r => new { r.Phase, r.Opponent }).Select(g => new
{
    g.Key.Phase, g.Key.Opponent, games = g.Count(), finished = g.Count(r => r.Reward is not null),
    wins = g.Count(r => r.Reward == 1), draws = g.Count(r => r.Reward == 0), losses = g.Count(r => r.Reward == -1),
    interrupted = g.Count(r => r.Reward is null),
    winRate = g.Any(r => r.Reward is not null) ? (double)g.Count(r => r.Reward == 1) / g.Count(r => r.Reward is not null) : 0
}).ToArray();

void Checkpoint(string stage)
{
    network.Save(Path.Combine(output, "weights.json"));
    File.WriteAllText(Path.Combine(output, "memory.json.tmp"), JsonSerializer.Serialize(memory));
    File.Move(Path.Combine(output, "memory.json.tmp"), Path.Combine(output, "memory.json"), true);
    var report = new { stage, now = DateTimeOffset.UtcNow, deadline, games = results.Count, updates = network.Updates, memories = memory.Count, recent = Summary(results.TakeLast(120)), totals = Summary(results) };
    string json = JsonSerializer.Serialize(report);
    progress.WriteLine(json);
    File.WriteAllText(Path.Combine(output, "report.json.tmp"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    File.Move(Path.Combine(output, "report.json.tmp"), Path.Combine(output, "report.json"), true);
    Console.WriteLine(json);
}

for (int i = 0; i < 12 && DateTimeOffset.UtcNow < deadline && !stopping; i++) Play("baseline", i, 0, false);
Checkpoint("baseline");
int trainingGames = 0;
var nextReport = DateTimeOffset.UtcNow.AddSeconds(30);
var nextEval = DateTimeOffset.UtcNow.AddMinutes(2);
// Reserve final two minutes for frozen-policy measurements; keep learning until that point.
while (!stopping && DateTimeOffset.UtcNow < deadline.AddMinutes(-2))
{
    double fraction = Math.Clamp((DateTimeOffset.UtcNow - started).TotalSeconds / (deadline - started).TotalSeconds, 0, 1);
    double epsilon = .30 * (1 - fraction) + .03;
    // Most play starts from the actual initial state; some real random openings broaden experience.
    Play("train", trainingGames++, epsilon, true, trainingGames % 5 == 0 ? 4 : 0);
    if (DateTimeOffset.UtcNow >= nextEval)
    {
        for (int i = 0; i < 4 && DateTimeOffset.UtcNow < deadline && !stopping; i++) Play("periodic", i, 0, true);
        for (int i = 0; i < 4 && DateTimeOffset.UtcNow < deadline && !stopping; i++) Play("periodic-neural", i, 0, false);
        nextEval = DateTimeOffset.UtcNow.AddMinutes(2);
    }
    if (DateTimeOffset.UtcNow >= nextReport) { Checkpoint("learning"); nextReport = DateTimeOffset.UtcNow.AddSeconds(30); }
}
Checkpoint("frozen-final");
network.Save(Path.Combine(output, "evaluated-weights.json"));
File.WriteAllText(Path.Combine(output, "evaluated-memory.json"), JsonSerializer.Serialize(memory));
foreach (string phase in new[] { "final-memory", "final-neural", "final-new-openings" })
    for (int i = 0; i < 60 && !stopping && DateTimeOffset.UtcNow < deadline; i++)
        Play(phase, i, 0, phase != "final-neural", phase == "final-new-openings" ? 4 : 0);
// If frozen measurements finish early, continue actual experience until the wall-clock deadline.
while (!stopping && DateTimeOffset.UtcNow < deadline) Play("train", trainingGames++, .03, true);
Checkpoint("complete");
File.WriteAllText(Path.Combine(output, "weights.sha256"), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "weights.json")))).ToLowerInvariant());

record Episode(int[][] States, float Reward);
record Result(string MatchId, string Phase, string Opponent, int Side, int? Reward, int Plies, int MemoryMoves, long UpdatesBefore, DateTimeOffset FinishedAt);
