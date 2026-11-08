using System.Diagnostics;
using System.Text.Json;
using Dama.Engine;

namespace Opale.C;

public static class Verification
{
    public static void Run()
    {
        var watch = Stopwatch.StartNew();
        var definition = ManualBuilder.Build();
        var net = new Network(definition);
        var checks = new List<string>();
        void Assert(bool ok, string label) { if (!ok) throw new InvalidOperationException("FAIL: " + label); checks.Add(label); }
        static Position Board(params (int Square, int Piece)[] pieces)
        {
            var board = new int[32]; foreach (var (square, piece) in pieces) board[square - 1] = piece;
            return new Position(board);
        }
        void CaptureCheck(string label, Position p, double expected)
        {
            var legal = Rules.LegalMoves(p);
            double actual = net.Feature(p.Board, p.Turn, "capture-max:0");
            double referee = legal.Count == 0 ? 0 : legal.Max(m => m.Captures.Sum(s => Math.Abs(p.Board[s]) == 2 ? 310 : 100));
            Assert(actual == expected && referee == expected, $"{label}: neural={actual}, referee={referee}");
        }
        CaptureCheck("man cannot take king", Board((18, 1), (14, -2)), 0);
        CaptureCheck("two attackers one victim", Board((21, 1), (22, 1), (18, -1)), 100);
        CaptureCheck("two independent alternatives", Board((21, 1), (24, 1), (18, -1), (20, -1)), 100);
        CaptureCheck("two actual consecutive captures", Board((21, 1), (18, -1), (11, -1)), 200);
        CaptureCheck("second victim king stops man", Board((21, 1), (18, -1), (11, -2)), 100);
        CaptureCheck("king captures both ranks", Board((21, 2), (18, -1), (11, -2)), 410);
        CaptureCheck("promotion ends the sequence", Board((10, 1), (6, -1), (7, -1)), 100);
        CaptureCheck("king does not stop on promotion row", Board((10, 2), (6, -1), (7, -1)), 200);
        CaptureCheck("length precedes victim material", Board((21, 2), (24, 2), (7, 1), (18, -2), (20, -1), (11, -1)), 200);
        void PromotionCheck(string label, Position p, double expected)
        {
            var legal = Rules.LegalMoves(p);
            double actual = net.Feature(p.Board, p.Turn, "capture-promotes:0");
            double referee = legal.Any(m => m.Captures.Length > 0 && m.Promotes) ? 1 : 0;
            Assert(actual == expected && referee == expected, $"{label}: promotion={actual}, referee={referee}");
        }
        PromotionCheck("single capture promotes", Board((10, 1), (6, -1)), 1);
        PromotionCheck("double capture promotes", Board((18, 1), (14, -1), (7, -1)), 1);
        PromotionCheck("king never gains promotion again", Board((10, 2), (6, -1)), 0);
        PromotionCheck("king priority excludes promoting man", Board((10, 1), (6, -1), (21, 2), (18, -1)), 0);
        PromotionCheck("longer capture excludes short promotion", Board((10, 1), (6, -1), (21, 1), (18, -1), (11, -1)), 0);
        var trap = Board((13, 1), (18, 1), (22, 1), (9, -1), (14, -1));
        var forced = Rules.LegalMoves(trap);
        Assert(forced.Count == 1 && forced[0].Id == "18-11", "AGENTE-B forced capture");
        var after = Rules.Apply(trap, forced[0]);
        Assert(Rules.LegalMoves(after).Single().Id == "9-18-27", "AGENTE-B vacated square opens double");
        CaptureCheck("AGENTE-B double detected from actual afterstate", after, 200);
        Assert(JsonSerializer.Serialize(definition, Network.Json) == JsonSerializer.Serialize(ManualBuilder.Build(), Network.Json), "deterministic manual builder");
        var roundTrip = new Network(JsonSerializer.Deserialize<Definition>(JsonSerializer.Serialize(definition, Network.Json), Network.Json)!);
        Assert(roundTrip.Evaluate(trap.Board, 1) == net.Evaluate(trap.Board, 1), "JSON round trip");
        int compared = 0, longer = 0;
        var random = new Random(389247);
        // Synthetic arithmetic/rule fixtures, never played games and never training data.
        for (int n = 0; n < 2500; n++)
        {
            var board = new int[32];
            for (int k = 0; k < 3 + n % 10; k++)
            {
                int s = random.Next(32), piece = new[] { 1, 2, -1, -2 }[random.Next(4)];
                if (piece == 1 && s < 4 || piece == -1 && s >= 28) piece *= 2;
                board[s] = piece;
            }
            int side = n % 2 == 0 ? 1 : -1;
            var p = new Position(board, side);
            var legal = Rules.LegalMoves(p);
            if (legal.Count != 0 && legal[0].Captures.Length > 2) { longer++; continue; }
            double expected = legal.Count == 0 ? 0 : legal.Max(m => m.Captures.Sum(s => Math.Abs(board[s]) == 2 ? 310 : 100));
            double actual = net.Feature(board, side, "capture-max:0");
            if (actual != expected) throw new InvalidOperationException($"Capture fixture mismatch {n}: expected {expected}, got {actual}; side {side}; board {string.Join(',', board)}");
            double expectedPromotion = legal.Any(m => m.Captures.Length > 0 && m.Promotes) ? 1 : 0;
            if (net.Feature(board, side, "capture-promotes:0") != expectedPromotion) throw new InvalidOperationException($"Promotion fixture mismatch {n}");
            var rotated = board.Reverse().Select(x => -x).ToArray();
            if (net.Evaluate(board, side) != net.Evaluate(rotated, -side)) throw new InvalidOperationException("Rotation/colour mismatch.");
            compared++;
        }
        var model = new Model(net);
        var initial = Position.Initial(); var initialLegal = Rules.LegalMoves(initial);
        var move = model.Choose(initial, initialLegal);
        Assert(initialLegal.Contains(move.Move), "legal opening decision");
        var kingCapture = Board((21, 2), (18, -2));
        Assert(model.Choose(kingCapture, Rules.LegalMoves(kingCapture)).Score == 100000, "terminal win overrides network");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        bool cancelled = false;
        try { model.Choose(initial, initialLegal, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "cancellation observed");
        Parallel.For(0, 32, _ => { if (model.Choose(initial, initialLegal).Move.Id != move.Move.Id) throw new InvalidOperationException("Concurrent nondeterminism."); });
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, units = definition.Units.Length, parameters = net.Parameters, checks, comparedSyntheticPositions = compared, excludedBeyondTwoCaptures = longer, elapsedMs = watch.ElapsedMilliseconds, gamesPlayed = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
