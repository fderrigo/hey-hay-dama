using System.Text.Json;
using Dama.Engine;
using Dama.Competition;

namespace Dama.Learn;

public static class Evolution
{
    public static void Run(string output, DateTimeOffset deadline, string? resume = null)
    {
        if (Directory.Exists(output)) throw new InvalidOperationException("Fresh output required.");
        Directory.CreateDirectory(output);
        var original = NeuralModel.Load("src/Dama.Web/Models/opale-v1.json");
        var champion = resume is null ? original : NeuralModel.Load(resume);
        var kimberlite = Dama.Neural.NeuralNetwork.Load("src/Dama.Neural/Models/kimberlite-v1.json");
        var agata = Agata.Network.Load("models/Agata/Agata/Models/agata-v1.json");
        var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
        var rng = new Random(6112026);
        using var log = new StreamWriter(Path.Combine(output, "games.jsonl")) { AutoFlush = true };
        using var progress = new StreamWriter(Path.Combine(output, "progress.jsonl")) { AutoFlush = true };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { started = DateTimeOffset.UtcNow, deadline, competitionDirectory = store.DirectoryPath, learner = "opale", opponents = new[] { "kimberlite", "agata" }, initialization = "Original manually constructed, never pretrained Opale weights. No dataset. Original evaluations are never supervision targets.", algorithm = "Evolutionary random mutations of output weights, accepted only by paired actual game outcomes. Same openings/colours/opponents for parent and candidate. No self-play. Opponent scores never consumed.", seed = 6112026 }));
        int total = 0, generation = 0, accepted = 0;
        bool stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        bool Available() => !stop && DateTimeOffset.UtcNow < deadline && !File.Exists(Path.Combine(output, "STOP"));
        (int Wins, int Draws, int Count) Suite(NeuralModel model, string phase, int suiteSeed, int count)
        {
            int wins = 0, draws = 0, played = 0;
            for (int index = 0; index < count && Available(); index++)
            {
                int side = index % 2 == 0 ? 1 : -1;
                string opponent = index % 4 < 2 ? "kimberlite" : "agata";
                var random = new Random(suiteSeed + index / 4);
                var own = Participant.Ai("opale", "Opale"); var other = Participant.Ai(opponent, opponent);
                using var tracked = TryStart(() => store.Start("benchmark", side == 1 ? own : other, side == 1 ? other : own), Available);
                if (tracked is null) break;
                using var transcript = new StreamWriter(Path.Combine(output, tracked.Id + ".jsonl")) { AutoFlush = true };
                var p = Position.Initial();
                var seen = new Dictionary<string, int> { [p.Key] = 1 };
                transcript.WriteLine(JsonSerializer.Serialize(new { kind = "start", matchId = tracked.Id, phase, generation, suiteSeed, index, opponent, side, board = p.Board, p.Ply, p.Turn, p.QuietKingPlyCount }));
                Outcome? outcome = null;
                while (Available() && p.Ply < 2000)
                {
                    var legal = Rules.LegalMoves(p);
                    outcome = Rules.GetOutcome(p, seen[p.Key], legal);
                    if (outcome is not null) break;
                    Move move;
                    if (index >= 4 && p.Ply < 4) move = legal[random.Next(legal.Count)];
                    else if (p.Turn == side) move = model.Choose(p, legal).Move;
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
                    p = Rules.Apply(p, move); seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                    transcript.WriteLine(JsonSerializer.Serialize(new { kind = "move", move = move.Id, board = p.Board, p.Ply, p.Turn, p.QuietKingPlyCount }));
                }
                outcome ??= Rules.GetOutcome(p, seen[p.Key]);
                Retry(() => { if (outcome is not null) tracked.Complete(outcome.Winner, p.Ply, outcome.Reason); else tracked.Interrupt(p.Ply, "evolution-deadline-or-stop"); return true; });
                transcript.WriteLine(JsonSerializer.Serialize(new { kind = "end", outcome, p.Ply }));
                int? reward = outcome is null ? null : outcome.Winner * side;
                if (reward == 1) wins++; if (reward == 0) draws++;
                if (reward is not null) played++;
                log.WriteLine(JsonSerializer.Serialize(new Result(tracked.Id, phase, opponent, side, reward, p.Ply, 0, accepted, DateTimeOffset.UtcNow)));
                total++;
            }
            return (wins, draws, played);
        }
        void Save(string name, NeuralModel model) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(model.Weights, NeuralModel.JsonOptions));
        Save("initial-weights.json", original);
        Save("weights.json", champion);
        var baseline = Suite(original, "baseline", 93000, 52);
        int bestValidation = baseline.Wins * 100 + baseline.Draws;
        Save("best-validation.json", champion);
        int suiteSeed = 100000;
        var parentResult = Suite(champion, "parent", suiteSeed, 52);
        while (Available() && DateTimeOffset.UtcNow < deadline.AddSeconds(-50))
        {
            generation++;
            // Mutation scale is algorithmic, not a tactical feature or external label.
            double sigma = generation % 5 == 0 ? .50 : generation % 3 == 0 ? .20 : .07;
            var mutated = champion.Weights.Hidden.Select(n => n with { OutputWeight = n.OutputWeight + (Math.Abs(n.OutputWeight) + 1) * sigma * Gaussian(rng) }).ToArray();
            var candidate = new NeuralModel(champion.Weights with { Name = "Opale - outcome evolution 2026-11-06", Construction = "Original hand-built network; output weights evolved solely by registered game outcomes.", Hidden = mutated });
            var trialResult = Suite(candidate, "candidate", suiteSeed, 52);
            var priorResult = parentResult;
            bool improved = parentResult.Count == 52 && trialResult.Count == 52 && (trialResult.Wins > parentResult.Wins || trialResult.Wins == parentResult.Wins && trialResult.Draws > parentResult.Draws);
            if (improved) { champion = candidate; parentResult = trialResult; accepted++; Save("weights.json", champion); }
            object? validation = null;
            if (generation % 10 == 0 && Available())
            {
                var v = Suite(champion, "validation", 93000, 52);
                int score = v.Wins * 100 + v.Draws;
                if (v.Count == 52 && score > bestValidation) { bestValidation = score; Save("best-validation.json", champion); }
                validation = new { v.Wins, v.Draws, v.Count, bestValidation };
            }
            if (generation % 30 == 0 && Available())
            {
                champion = NeuralModel.Load(Path.Combine(output, "best-validation.json"));
                suiteSeed += 100;
                parentResult = Suite(champion, "parent", suiteSeed, 52);
            }
            var row = new { now = DateTimeOffset.UtcNow, generation, accepted, totalGames = total, sigma, improved, parent = new { priorResult.Wins, priorResult.Draws }, candidate = new { trialResult.Wins, trialResult.Draws }, validation };
            string json = JsonSerializer.Serialize(row); progress.WriteLine(json);
            if (generation % 10 == 0) Console.WriteLine(json);
        }
        // Freeze the best validation-selected network, then measure new held-out openings.
        champion = NeuralModel.Load(Path.Combine(output, "best-validation.json"));
        Save("opale-learned.json", champion);
        var originalFinal = Suite(original, "final-original", 730001, 52);
        var learnedFinal = Suite(champion, "final-learned", 730001, 52);
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { ended = DateTimeOffset.UtcNow, deadline, generation, accepted, totalGames = total, baseline = new { baseline.Wins, baseline.Draws, baseline.Count }, originalFinal = new { originalFinal.Wins, originalFinal.Draws, originalFinal.Count }, learnedFinal = new { learnedFinal.Wins, learnedFinal.Draws, learnedFinal.Count }, bestValidation }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(Path.Combine(output, "report.json")));
    }
    private static double Gaussian(Random rng) => Math.Sqrt(-2 * Math.Log(Math.Max(1e-12, rng.NextDouble()))) * Math.Cos(2 * Math.PI * rng.NextDouble());
    private static T Retry<T>(Func<T> action)
    {
        while (true)
        {
            try { return action(); }
            catch (IOException ex) when (ex.Message.Contains(".lock", StringComparison.OrdinalIgnoreCase))
            { Console.WriteLine("Shared competition registry busy; retrying without playing unregistered games."); Thread.Sleep(100); }
        }
    }
    internal static TrackedMatch? TryStart(Func<TrackedMatch> action, Func<bool> available)
    {
        while (available())
        {
            try { return action(); }
            catch (IOException ex) when (ex.Message.Contains(".lock", StringComparison.OrdinalIgnoreCase)) { Thread.Sleep(100); }
        }
        return null;
    }
}
