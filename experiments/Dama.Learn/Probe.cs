using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

namespace Dama.Learn;

// Frozen, registered evaluation. No observations from this probe enter training.
public static class Probe
{
    public static void Run(string weightsPath, string output, DateTimeOffset deadline, string? memoryPath = null, int gameCount = 52)
    {
        if (Directory.Exists(output)) throw new InvalidOperationException("Probe output must be new.");
        Directory.CreateDirectory(output);
        var model = NeuralModel.Load(weightsPath);
        var memory = memoryPath is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(memoryPath))!;
        var kimberlite = Dama.Neural.NeuralNetwork.Load("src/Dama.Neural/Models/kimberlite-v1.json");
        var agata = Agata.Network.Load("models/Agata/Agata/Models/agata-v1.json");
        var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { weightsPath, memoryPath, deadline, competitionDirectory = store.DirectoryPath, seed = 83000, training = false, protocol = memoryPath is null ? "4 standard-start games, then 12 seeded four-ply openings per opponent and colour" : "Learned winning continuations; depth-6 search up to 250ms on unmemorized positions; both opponents and colours" }));
        var results = new List<Result>();
        using var log = new StreamWriter(Path.Combine(output, "games.jsonl")) { AutoFlush = true };
        for (int index = 0; index < gameCount && DateTimeOffset.UtcNow < deadline; index++)
        {
            int side = index % 2 == 0 ? 1 : -1;
            string opponent = index % 4 < 2 ? "kimberlite" : "agata";
            string phase = index < 4 ? "probe-standard" : "probe-openings";
            var random = new Random(83000 + index / 4);
            var own = Participant.Ai("opale", "Opale");
            var other = Participant.Ai(opponent, opponent);
            using var tracked = Evolution.TryStart(() => store.Start("benchmark", side == 1 ? own : other, side == 1 ? other : own), () => DateTimeOffset.UtcNow < deadline);
            if (tracked is null) break;
            using var transcript = new StreamWriter(Path.Combine(output, tracked.Id + ".jsonl")) { AutoFlush = true };
            var p = Position.Initial();
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            transcript.WriteLine(JsonSerializer.Serialize(new { kind = "start", matchId = tracked.Id, phase, opponent, side, board = p.Board, p.Ply, p.Turn, p.QuietKingPlyCount }));
            Outcome? outcome = null;
            while (DateTimeOffset.UtcNow < deadline && p.Ply < 2000)
            {
                var legal = Rules.LegalMoves(p);
                outcome = Rules.GetOutcome(p, seen[p.Key], legal);
                if (outcome is not null) break;
                Move move;
                if (index >= 4 && p.Ply < 4) move = legal[random.Next(legal.Count)];
                else if (p.Turn == side)
                {
                    string key = $"{opponent}:{side}:{p.Key}:{p.QuietKingPlyCount}";
                    if (memoryPath is not null && memory.TryGetValue(key, out var remembered) && legal.FirstOrDefault(m => m.Id == remembered) is { } known) move = known;
                    else if (memoryPath is not null) move = new SearchModel(model).Choose(p, legal, 6, Math.Min(250, Math.Max(1, (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds))).Move;
                    else move = model.Choose(p, legal).Move;
                }
                else if (opponent == "agata")
                {
                    var candidates = legal.Select(m => new Agata.Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
                    move = legal[Agata.Policy.Choose(Agata.Policy.Values(agata, p.Board, p.Turn, candidates), 0, random)];
                }
                else
                {
                    move = legal[0]; double best = double.NegativeInfinity;
                    foreach (var candidate in legal)
                    {
                        var next = Rules.Apply(p, candidate);
                        var terminal = Rules.GetOutcome(next);
                        double score = terminal is { Winner: 0 } ? 0 : terminal is not null ? 100000 : kimberlite.Evaluate(next, p.Turn);
                        if (score > best) { best = score; move = candidate; }
                    }
                }
                p = Rules.Apply(p, move);
                seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                transcript.WriteLine(JsonSerializer.Serialize(new { kind = "move", move = move.Id, board = p.Board, p.Ply, p.Turn, p.QuietKingPlyCount }));
            }
            outcome ??= Rules.GetOutcome(p, seen[p.Key]);
            Retry(() => { if (outcome is not null) tracked.Complete(outcome.Winner, p.Ply, outcome.Reason); else tracked.Interrupt(p.Ply, "probe-deadline-or-safety-limit"); return true; });
            transcript.WriteLine(JsonSerializer.Serialize(new { kind = "end", outcome, p.Ply }));
            var row = new Result(tracked.Id, phase, opponent, side, outcome is null ? null : outcome.Winner * side, p.Ply, 0, 0, DateTimeOffset.UtcNow);
            results.Add(row); log.WriteLine(JsonSerializer.Serialize(row));
        }
        var summary = results.GroupBy(r => new { r.Phase, r.Opponent }).Select(g => new { g.Key.Phase, g.Key.Opponent, games = g.Count(), wins = g.Count(r => r.Reward == 1), draws = g.Count(r => r.Reward == 0), losses = g.Count(r => r.Reward == -1), interrupted = g.Count(r => r.Reward is null) }).ToArray();
        string json = JsonSerializer.Serialize(new { weightsPath, results = summary }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "report.json"), json);
        Console.WriteLine(json);
    }
    private static T Retry<T>(Func<T> action)
    {
        while (true)
            try { return action(); }
            catch (IOException ex) when (ex.Message.Contains(".lock", StringComparison.OrdinalIgnoreCase)) { Thread.Sleep(100); }
    }
}
