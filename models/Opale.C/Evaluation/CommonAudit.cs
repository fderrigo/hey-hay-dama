using System.Text.Json;
using Dama.Engine;
using Dama.Competition;

namespace Opale.C;

// Replays existing records only. Never creates matches or calls model services.
public static class CommonAudit
{
    public static void Run(string directory)
    {
        var ids = new HashSet<string>();
        var pairs = new Dictionary<string, List<(string White, string Prefix)>>();
        var counts = new Dictionary<string, int[]>();
        int moves = 0, finished = 0, interrupted = 0;
        foreach (var line in File.ReadLines(Path.Combine(directory, "traiettorie.jsonl")))
        {
            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            string id = e.GetProperty("matchId").GetString()!;
            Require(ids.Add(id), "Unique match ID");
            string white = e.GetProperty("white").GetString()!, black = e.GetProperty("black").GetString()!;
            Require(white != black, "Distinct opponents");
            using var registered = JsonDocument.Parse(File.ReadAllText(Path.Combine(CompetitionStore.DefaultDirectory(), id + ".json")));
            var r = registered.RootElement;
            Require(r.GetProperty("source").GetString() == "benchmark", "Registry source");
            Require(r.GetProperty("white").GetProperty("id").GetString() == "ai:" + white && r.GetProperty("black").GetProperty("id").GetString() == "ai:" + black, "Registry players");
            var p = Position.Initial();
            var openingRandom = new Random(e.GetProperty("gameSeed").GetInt32());
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            var prefix = new List<string>();
            foreach (var f in e.GetProperty("frames").EnumerateArray())
            {
                Require(Rules.GetOutcome(p, seen[p.Key]) is null, "No move after terminal");
                var moveId = f.GetProperty("moveId").GetString();
                var legal = Rules.LegalMoves(p);
                var move = legal.Single(m => m.Id == moveId);
                if (p.Ply < 6) Require(legal[openingRandom.Next(legal.Count)].Id == moveId, "Opening seed");
                p = Rules.Apply(p, move);
                seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                Require(p.Ply == f.GetProperty("ply").GetInt32() && p.Turn == f.GetProperty("turn").GetInt32(), "Frame counters");
                Require(p.Board.SequenceEqual(f.GetProperty("board").EnumerateArray().Select(x => x.GetInt32())), "Frame board");
                if (p.Ply <= 6) prefix.Add(move.Id);
                moves++;
            }
            Require(p.Ply == e.GetProperty("plies").GetInt32() && p.Ply == r.GetProperty("plies").GetInt32(), "Move count");
            bool completed = e.GetProperty("completed").GetBoolean();
            Require(r.GetProperty("status").GetString() == (completed ? "finished" : "interrupted"), "Registry status");
            Require(r.GetProperty("reason").GetString() == e.GetProperty("reason").GetString(), "Registry reason");
            foreach (var player in new[] { white, black }) counts.TryAdd(player, new int[4]);
            if (completed)
            {
                var outcome = Rules.GetOutcome(p, seen[p.Key]);
                Require(outcome != null, "Natural outcome");
                Require(outcome!.Winner == e.GetProperty("winner").GetInt32() && outcome.Winner == r.GetProperty("winner").GetInt32(), "Winner");
                Require(outcome.Reason == e.GetProperty("reason").GetString(), "Referee reason");
                counts[white][outcome.Winner == 1 ? 0 : outcome.Winner == 0 ? 1 : 2]++;
                counts[black][outcome.Winner == -1 ? 0 : outcome.Winner == 0 ? 1 : 2]++;
                finished++;
            }
            else
            {
                if (e.GetProperty("reason").GetString() == "ply-limit")
                    Require(p.Ply == 240 && Rules.GetOutcome(p, seen[p.Key]) is null, "Ply limit is not a natural outcome");
                interrupted++; counts[white][3]++; counts[black][3]++;
            }
            string key = string.Join("|", new[] { white, black }.Order()) + "|" + e.GetProperty("gameSeed").GetInt32();
            if (!pairs.TryGetValue(key, out var pair)) pairs[key] = pair = [];
            pair.Add((white, string.Join(" ", prefix)));
        }
        foreach (var pair in pairs.Values)
        {
            Require(pair.Count <= 2, "No duplicated opening pair");
            if (pair.Count == 2) Require(pair[0].White != pair[1].White && pair[0].Prefix == pair[1].Prefix, "Paired colours and identical opening");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, matches = ids.Count, moves, finished, interrupted, completePairs = pairs.Values.Count(p => p.Count == 2), counts }, new JsonSerializerOptions { WriteIndented = true }));
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
