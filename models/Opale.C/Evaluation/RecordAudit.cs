using System.Security.Cryptography;
using System.Text.Json;
using Dama.Engine;

namespace Opale.C;

/// <summary>Read-only reconstruction of existing transcripts; never starts matches or updates weights.</summary>
public static class RecordAudit
{
    public static void Run(string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        string shared = manifest.RootElement.GetProperty("competitionDirectory").GetString()!;
        var deadline = manifest.RootElement.GetProperty("deadline").GetDateTimeOffset();
        foreach (var property in manifest.RootElement.GetProperty("hashes").EnumerateObject())
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, property.Name + ".json")))).Equals(property.Value.GetString(), StringComparison.OrdinalIgnoreCase), "Weight snapshot hash: " + property.Name);
        var rows = File.ReadLines(Path.Combine(directory, "games.jsonl")).Select(line => JsonSerializer.Deserialize<Benchmark.GameResult>(line)!).ToArray();
        Require(rows.Length == 96 && rows.Select(r => r.MatchId).Distinct().Count() == rows.Length, "96 unique matches");
        long moves = 0;
        var openingMoves = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(shared, row.MatchId + ".json")));
            var registry = record.RootElement;
            Require(registry.GetProperty("source").GetString() == "benchmark" && registry.GetProperty("status").GetString() == "finished", "Finished benchmark: " + row.MatchId);
            Require(registry.GetProperty("type").GetString() == "ai-ai", "AI participants");
            Require(registry.GetProperty(row.Side == 1 ? "white" : "black").GetProperty("id").GetString() == "ai:opale", "Opale identity");
            Require(registry.GetProperty(row.Side == 1 ? "black" : "white").GetProperty("id").GetString() == "ai:" + row.Opponent, "Opponent identity");
            Require(registry.GetProperty("updatedAt").GetDateTimeOffset() <= deadline, "Within frozen benchmark deadline");
            var p = Position.Initial();
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            var prefix = new List<string>();
            int prefixLength = 0;
            bool started = false, ended = false;
            foreach (var line in File.ReadLines(Path.Combine(directory, row.MatchId + ".jsonl")))
            {
                using var doc = JsonDocument.Parse(line); var e = doc.RootElement;
                Require(!ended, "No entries after end");
                switch (e.GetProperty("kind").GetString())
                {
                    case "start":
                        Require(!started && e.GetProperty("id").GetString() == row.MatchId, "Transcript identity");
                        Require(e.GetProperty("version").GetString() == row.Version && e.GetProperty("side").GetInt32() == row.Side, "Transcript version and colour");
                        started = true;
                        prefixLength = e.GetProperty("openingPlies").GetInt32();
                        break;
                    case "move":
                        Require(started && Rules.GetOutcome(p, seen[p.Key]) is null, "Only moves before terminal");
                        string id = e.GetProperty("move").GetString()!;
                        var move = Rules.LegalMoves(p).Single(m => m.Id == id);
                        p = Rules.Apply(p, move); seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                        if (p.Ply <= prefixLength) prefix.Add(id);
                        moves++;
                        break;
                    case "end":
                        var actual = Rules.GetOutcome(p, seen[p.Key]);
                        Require(actual is not null, "Natural referee outcome");
                        Require(e.GetProperty("outcome").GetProperty("Winner").GetInt32() == actual!.Winner && e.GetProperty("outcome").GetProperty("Reason").GetString() == actual.Reason, "Transcript outcome");
                        Require(registry.GetProperty("winner").GetInt32() == actual.Winner && registry.GetProperty("reason").GetString() == actual.Reason, "Shared registry outcome");
                        Require(row.Reward == actual.Winner * row.Side && row.Reason == actual.Reason, "Report reward");
                        ended = true;
                        continue;
                    default: throw new InvalidDataException("Unknown transcript entry.");
                }
                Require(e.GetProperty("board").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(p.Board), "Board after every move");
                Require(e.GetProperty("Ply").GetInt32() == p.Ply && e.GetProperty("Turn").GetInt32() == p.Turn && e.GetProperty("QuietKingPlyCount").GetInt32() == p.QuietKingPlyCount, "Position counters");
            }
            Require(started && ended && row.Plies == p.Ply && registry.GetProperty("plies").GetInt32() == p.Ply, "Complete transcript and move count");
            string opening = string.Join(' ', prefix);
            if (openingMoves.TryGetValue(row.Opening, out var previous)) Require(previous == opening, "Identical paired opening");
            else openingMoves.Add(row.Opening, opening);
        }
        foreach (var group in rows.GroupBy(r => new { r.Opening, r.Opponent, r.Side }))
            Require(group.Count() == 2 && group.Select(r => r.Version).Distinct().Count() == 2, "Two versions in each pair");
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "report.json")));
        foreach (var group in report.RootElement.GetProperty("results").EnumerateArray())
        {
            var matching = rows.Where(r => r.Version == group.GetProperty("version").GetString() && r.Opponent == group.GetProperty("opponent").GetString()).ToArray();
            Require(matching.Length == group.GetProperty("games").GetInt32() && matching.Count(r => r.Reward == 1) == group.GetProperty("wins").GetInt32() && matching.Count(r => r.Reward == 0) == group.GetProperty("draws").GetInt32() && matching.Count(r => r.Reward == -1) == group.GetProperty("losses").GetInt32(), "Aggregated results");
        }
        string json = JsonSerializer.Serialize(new { success = true, auditedAt = DateTimeOffset.UtcNow, matches = rows.Length, verifiedMoves = moves, pairedComparisons = rows.Length / 2, distinctOpenings = openingMoves.Values.Distinct().Count(), sharedRegistryMatches = rows.Length, frozenHashesVerified = true, gamesPlayedDuringAudit = 0 }, Network.Json);
        File.WriteAllText(Path.Combine(directory, "audit.json"), json);
        Console.WriteLine(json);
    }
    private static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }
}
