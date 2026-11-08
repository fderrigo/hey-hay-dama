using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

namespace Dama.Learn;

public static class Audit
{
    public static void RepairInterrupted(string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var store = new CompetitionStore(manifest.RootElement.GetProperty("competitionDirectory").GetString()!);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (string path in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            string recordPath = Path.Combine(store.DirectoryPath, id + ".json");
            var record = JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(recordPath), options)!;
            if (record.Status != "active") continue;
            var lines = ReadSharedLines(path);
            using var last = JsonDocument.Parse(lines[^1]);
            int ply = last.RootElement.GetProperty("Ply").GetInt32();
            store.Save(record with { Status = "interrupted", Winner = null, Reason = "user-corrected-learner-identity", Plies = ply, UpdatedAt = DateTimeOffset.UtcNow });
            File.AppendAllText(path, JsonSerializer.Serialize(new { kind = "end", outcome = (Outcome?)null, Ply = ply }) + Environment.NewLine);
            Console.WriteLine("Interrupted " + id + " at ply " + ply);
        }
    }

    // Reconstructs already recorded games only. No games are started, played or trained here.
    public static void Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var shared = manifest.RootElement.GetProperty("competitionDirectory").GetString()!;
        var deadline = manifest.RootElement.GetProperty("deadline").GetDateTimeOffset();
        var ids = new HashSet<string>();
        long moves = 0, finished = 0, interrupted = 0;
        DateTimeOffset latest = default;
        foreach (var line in ReadSharedLines(Path.Combine(directory, "games.jsonl")))
        {
            using var result = JsonDocument.Parse(line);
            var r = result.RootElement;
            var id = r.GetProperty("MatchId").GetString()!;
            Require(ids.Add(id), "duplicate match " + id);
            using var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(shared, id + ".json")));
            var registered = registry.RootElement;
            Require(registered.GetProperty("source").GetString() == "benchmark", "source " + id);
            Require(registered.GetProperty("type").GetString() == "ai-ai", "type " + id);
            Require(registered.GetProperty("startedAt").GetDateTimeOffset() <= deadline, "started after deadline " + id);
            var p = Position.Initial();
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            bool ended = false;
            foreach (var entry in ReadSharedLines(Path.Combine(directory, id + ".jsonl")))
            {
                using var doc = JsonDocument.Parse(entry);
                var e = doc.RootElement;
                var kind = e.GetProperty("kind").GetString();
                Require(!ended, "entry after end " + id);
                if (kind == "start")
                    Require(e.GetProperty("matchId").GetString() == id && p.Ply == 0, "start " + id);
                else if (kind == "move")
                {
                    Require(Rules.GetOutcome(p, seen[p.Key]) is null, "move after terminal " + id);
                    string moveId = e.GetProperty("move").GetString()!;
                    var move = Rules.LegalMoves(p).Single(m => m.Id == moveId);
                    p = Rules.Apply(p, move);
                    seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                    moves++;
                }
                else if (kind == "end")
                {
                    ended = true;
                    var outcome = Rules.GetOutcome(p, seen[p.Key]);
                    if (e.GetProperty("outcome").ValueKind == JsonValueKind.Null)
                    {
                        Require(registered.GetProperty("status").GetString() == "interrupted", "interruption " + id);
                        Require(registered.GetProperty("winner").ValueKind == JsonValueKind.Null, "interrupted winner " + id);
                        interrupted++;
                    }
                    else
                    {
                        Require(outcome is not null, "fabricated terminal " + id);
                        Require(registered.GetProperty("status").GetString() == "finished", "not finished " + id);
                        Require(registered.GetProperty("winner").GetInt32() == outcome!.Winner, "winner " + id);
                        Require(registered.GetProperty("reason").GetString() == outcome.Reason, "reason " + id);
                        Require(r.GetProperty("Reward").GetInt32() == outcome.Winner * r.GetProperty("Side").GetInt32(), "reward " + id);
                        finished++;
                    }
                    continue;
                }
                Require(e.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(p.Board), "board " + id);
                Require(e.GetProperty("Ply").GetInt32() == p.Ply && e.GetProperty("Turn").GetInt32() == p.Turn && e.GetProperty("QuietKingPlyCount").GetInt32() == p.QuietKingPlyCount, "position counters " + id);
            }
            Require(ended, "missing end " + id);
            Require(registered.GetProperty("plies").GetInt32() == p.Ply && r.GetProperty("Plies").GetInt32() == p.Ply, "plies " + id);
            var time = r.GetProperty("FinishedAt").GetDateTimeOffset();
            if (time > latest) latest = time;
        }
        long updates = 0;
        string weightsPath = Path.Combine(directory, "weights.json");
        if (File.Exists(weightsPath))
        {
            using var weightsDocument = JsonDocument.Parse(File.ReadAllText(weightsPath));
            if (weightsDocument.RootElement.TryGetProperty("format", out _)) _ = NeuralModel.Load(weightsPath);
            else
            {
                var weights = JsonSerializer.Deserialize<ValueNetwork>(File.ReadAllText(weightsPath))!;
                Require(weights.W.All(float.IsFinite) && weights.B.All(float.IsFinite) && weights.O.All(float.IsFinite) && weights.Linear.All(float.IsFinite) && float.IsFinite(weights.Bias), "nonfinite weights");
                updates = weights.Updates;
            }
        }
        string json = JsonSerializer.Serialize(new { auditedAt = DateTimeOffset.UtcNow, matches = ids.Count, finished, interrupted, verifiedMoves = moves, latestMatchEnd = latest, deadline, finiteWeights = File.Exists(weightsPath), updates });
        File.WriteAllText(Path.Combine(directory, "audit.json"), json);
        Console.WriteLine(json);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static string[] ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        int lastNewline = text.LastIndexOf('\n');
        return lastNewline < 0 ? [] : text[..lastNewline].Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
