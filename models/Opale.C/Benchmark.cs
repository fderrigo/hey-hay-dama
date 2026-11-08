using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

namespace Opale.C;

/// <summary>One frozen paired comparison. Never modifies a parameter or selects a checkpoint.</summary>
public static class Benchmark
{
    public sealed record GameResult(string MatchId, string Version, string Opponent, int Opening, int Side, int? Reward, int Plies, string Reason);
    public static void Run(string directory, DateTimeOffset deadline)
    {
        if (Directory.Exists(directory)) throw new InvalidOperationException("Use a new result directory.");
        Directory.CreateDirectory(directory);
        var files = new Dictionary<string, string>
        {
            ["opale-v1"] = "src/Dama.Web/Models/opale-v1.json",
            ["opale-c-v2"] = "models/Opale.C/Models/opale-c-v2.json",
            ["kimberlite"] = "src/Dama.Neural/Models/kimberlite-v1.json",
            ["agata"] = "models/Agata/Agata/Models/agata-v1.json"
        };
        var hashes = new Dictionary<string, string>();
        foreach (var (name, path) in files)
        {
            string snapshot = Path.Combine(directory, name + ".json");
            File.Copy(path, snapshot);
            hashes[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(snapshot))).ToLowerInvariant();
        }
        var original = NeuralModel.Load(Path.Combine(directory, "opale-v1.json"));
        var candidate = new Model(Network.Load(Path.Combine(directory, "opale-c-v2.json")));
        var kimberlite = Dama.Neural.NeuralNetwork.Load(Path.Combine(directory, "kimberlite.json"));
        var agata = Agata.Network.Load(Path.Combine(directory, "agata.json"));
        var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
        const int seed = 2026110639;
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
        {
            agent = "AGENTE-C", started = DateTimeOffset.UtcNow, deadline, seed, hashes,
            competitionDirectory = store.DirectoryPath,
            protocol = "12 new seeded legal openings of 4/6/8 plies; paired colours and versions; Kimberlite and Agata temperature 0; 96 planned games. Each opening is actually played and registered. No parameter updates, no self-play, natural referee outcomes only.",
            limitation = "Small paired comparison of frozen opponent versions; opening pairs are correlated. Not an Elo or tournament-strength estimate."
        }, Network.Json));
        bool stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        bool Available() => !stop && DateTimeOffset.UtcNow < deadline;
        using var log = new StreamWriter(Path.Combine(directory, "games.jsonl")) { AutoFlush = true };
        var results = new List<GameResult>();
        var latencies = new Dictionary<string, List<double>> { ["opale-v1"] = [], ["opale-c-v2"] = [] };
        for (int opening = 0; opening < 12 && Available(); opening++)
        foreach (string opponent in new[] { "kimberlite", "agata" })
        foreach (int side in new[] { 1, -1 })
        foreach (string version in new[] { "opale-v1", "opale-c-v2" })
        {
            if (!Available()) break;
            var own = Participant.Ai("opale", "Opale"); var other = Participant.Ai(opponent, opponent);
            using var match = Start(store, side == 1 ? own : other, side == 1 ? other : own, Available);
            if (match is null) break;
            using var transcript = new StreamWriter(Path.Combine(directory, match.Id + ".jsonl")) { AutoFlush = true };
            var p = Position.Initial();
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            var random = new Random(seed + opening);
            int openingPlies = 4 + 2 * (opening % 3);
            transcript.WriteLine(JsonSerializer.Serialize(new { kind = "start", id = match.Id, version, opponent, side, opening, openingPlies, seed = seed + opening, board = p.Board, p.Turn, p.Ply, p.QuietKingPlyCount }));
            Outcome? outcome = null;
            while (Available() && p.Ply < 2000)
            {
                var legal = Rules.LegalMoves(p);
                outcome = Rules.GetOutcome(p, seen[p.Key], legal);
                if (outcome is not null) break;
                Move move;
                if (p.Ply < openingPlies) move = legal[random.Next(legal.Count)];
                else if (p.Turn == side)
                {
                    long start = Stopwatch.GetTimestamp();
                    move = version == "opale-v1" ? original.Choose(p, legal).Move : candidate.Choose(p, legal).Move;
                    latencies[version].Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                else if (opponent == "agata")
                {
                    var choices = legal.Select(m => new Agata.Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
                    move = legal[Agata.Policy.Choose(Agata.Policy.Values(agata, p.Board, p.Turn, choices), 0, random)];
                }
                else
                {
                    move = legal[0]; double best = double.NegativeInfinity;
                    foreach (var m in legal)
                    {
                        var next = Rules.Apply(p, m); var terminal = Rules.GetOutcome(next);
                        double score = terminal is { Winner: 0 } ? 0 : terminal is not null ? 100000 : kimberlite.Evaluate(next, p.Turn);
                        if (score > best) { best = score; move = m; }
                    }
                }
                if (!legal.Contains(move)) throw new InvalidOperationException("Illegal move.");
                p = Rules.Apply(p, move); seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                transcript.WriteLine(JsonSerializer.Serialize(new { kind = "move", move = move.Id, board = p.Board, p.Turn, p.Ply, p.QuietKingPlyCount }));
            }
            outcome ??= Rules.GetOutcome(p, seen[p.Key]);
            Finish(() => { if (outcome is not null) match.Complete(outcome.Winner, p.Ply, outcome.Reason); else match.Interrupt(p.Ply, "session-time-or-safety-limit"); });
            transcript.WriteLine(JsonSerializer.Serialize(new { kind = "end", outcome, p.Ply }));
            var row = new GameResult(match.Id, version, opponent, opening, side, outcome is null ? null : outcome.Winner * side, p.Ply, outcome?.Reason ?? "interrupted");
            results.Add(row); log.WriteLine(JsonSerializer.Serialize(row));
            if (results.Count % 8 == 0) Console.WriteLine(JsonSerializer.Serialize(new { games = results.Count, now = DateTimeOffset.UtcNow, results = Summary(results) }));
        }
        foreach (var (name, hash) in hashes)
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name + ".json")))).ToLowerInvariant() != hash)
                throw new InvalidOperationException("A frozen weight file changed.");
        var timing = latencies.ToDictionary(x => x.Key, x =>
        {
            var sorted = x.Value.Order().ToArray();
            return new { moves = sorted.Length, medianMs = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2], p95Ms = sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .95))], maxMs = sorted.Length == 0 ? 0 : sorted[^1] };
        });
        string report = JsonSerializer.Serialize(new { finishedAt = DateTimeOffset.UtcNow, games = results.Count, weightsUnchanged = true, results = Summary(results), timing }, Network.Json);
        File.WriteAllText(Path.Combine(directory, "report.json"), report);
        Console.WriteLine(report);
    }

    private static object Summary(IEnumerable<GameResult> results) => results.GroupBy(r => new { r.Version, r.Opponent }).Select(g => new
    {
        g.Key.Version, g.Key.Opponent, games = g.Count(), wins = g.Count(r => r.Reward == 1), draws = g.Count(r => r.Reward == 0), losses = g.Count(r => r.Reward == -1), interrupted = g.Count(r => r.Reward is null),
        winRate = g.Any(r => r.Reward is not null) ? (double)g.Count(r => r.Reward == 1) / g.Count(r => r.Reward is not null) : 0,
        scoreRate = g.Any(r => r.Reward is not null) ? (g.Count(r => r.Reward == 1) + .5 * g.Count(r => r.Reward == 0)) / g.Count(r => r.Reward is not null) : 0
    }).ToArray();

    private static TrackedMatch? Start(CompetitionStore store, Participant white, Participant black, Func<bool> available)
    {
        while (available())
        {
            try { return store.Start("benchmark", white, black); }
            catch (IOException ex) when (ex.Message.Contains(".lock", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Registry busy; no game started. Waiting for registration."); }
        }
        return null;
    }
    private static void Finish(Action action)
    {
        while (true)
        {
            try { action(); return; }
            catch (IOException ex) when (ex.Message.Contains(".lock", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Game ended; waiting to persist its actual result."); }
        }
    }
}
