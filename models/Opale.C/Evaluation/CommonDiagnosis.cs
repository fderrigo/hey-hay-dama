using System.Text.Json;
using Dama.Engine;

namespace Opale.C;

public static class CommonDiagnosis
{
    public static void Run(string directory)
    {
        var net = Network.Load("models/Opale.C/Models/opale-c-v2.json");
        int states = 0, mismatches = 0;
        foreach (var line in File.ReadLines(Path.Combine(directory, "traiettorie.jsonl")))
        {
            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            if (e.GetProperty("white").GetString() != "opale" && e.GetProperty("black").GetString() != "opale") continue;
            int opale = e.GetProperty("white").GetString() == "opale" ? 1 : -1;
            var p = Position.Initial();
            var game = e.GetProperty("matchId").GetString();
            foreach (var f in e.GetProperty("frames").EnumerateArray())
            {
                if (game!.StartsWith("de610040") && p.Ply >= 120 && p.Turn == opale)
                {
                    var candidates = Rules.LegalMoves(p).Select(m => new { m.Id, score = -net.Evaluate(Rules.Apply(p, m).Board, -p.Turn) }).OrderByDescending(x => x.score).ToArray();
                    Console.WriteLine(JsonSerializer.Serialize(new { kind = "repetition-candidates", game, p.Ply, chosen = f.GetProperty("moveId").GetString(), candidates }));
                }
                var move = Rules.LegalMoves(p).Single(m => m.Id == f.GetProperty("moveId").GetString());
                p = Rules.Apply(p, move);
                var legal = Rules.LegalMoves(p);
                if (p.Turn == opale || legal.Count == 0) continue;
                states++;
                double actual = legal.Max(m => m.Captures.Sum(s => Math.Abs(p.Board[s]) == 2 ? 310d : 100d));
                double predicted = net.Feature(p.Board, p.Turn, "capture-max:0");
                if (legal.Any(m => m.Promotes && m.Captures.Length > 0))
                    Console.WriteLine(JsonSerializer.Serialize(new { kind = "future-capture-promotion", game, p.Ply, predicted, promotions = legal.Where(m => m.Promotes && m.Captures.Length > 0).Select(m => m.Id).ToArray() }));
                if (Math.Abs(actual - predicted) > .001)
                {
                    mismatches++;
                    Console.WriteLine(JsonSerializer.Serialize(new { game, p.Ply, actual, predicted, captureLength = legal.Max(m => m.Captures.Length), p.Board }));
                }
            }
        }
        Console.WriteLine($"Analyzed {states} opponent-to-move afterstates; capture discrepancies {mismatches}. No games played or weights changed.");
    }
}
