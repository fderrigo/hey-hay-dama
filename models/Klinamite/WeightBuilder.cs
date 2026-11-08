namespace Klinamite;

public static class WeightBuilder
{
    private const int Inputs = Network.InputCount, OwnMan = 0, OwnKing = 1, EnemyMan = 2, EnemyKing = 3, Empty = 4;
    private static readonly int[] Forward = [0, 1], Down = [2, 3], All = [0, 1, 2, 3];
    private static readonly int[] Advance = [0, 1, 2, 4, 7, 12, 20, 0];

    private static int Row(int square) => square / 4;
    private static int Column(int square) => square / 4 % 2 + 2 * (square % 4);
    private static int Square(int row, int column) => row is >= 0 and < 8 && column is >= 0 and < 8 && (row + column) % 2 == 0 ? row * 4 + column / 2 : -1;
    private static int Step(int square, int direction) => Square(Row(square) + (direction < 2 ? -1 : 1), Column(square) + (direction % 2 == 0 ? -1 : 1));
    private static int Jump(int square, int direction) => Square(Row(square) + (direction < 2 ? -2 : 2), Column(square) + (direction % 2 == 0 ? -2 : 2));

    private sealed class Graph
    {
        public readonly List<NeuronDefinition> Neurons = [];
        public int Add(int layer, float bias, IEnumerable<(int From, float Weight)> links, string? label = null)
        {
            var merged = links.GroupBy(l => l.From).Select(g => (From: g.Key, Weight: g.Sum(l => l.Weight))).OrderBy(l => l.From).ToArray();
            Neurons.Add(new NeuronDefinition(layer, bias, merged.Select(l => l.From).ToArray(), merged.Select(l => l.Weight).ToArray(), label));
            return Inputs + Neurons.Count - 1;
        }
        public int Condition(IEnumerable<int> present, IEnumerable<int> absent, string? label = null)
        {
            var yes = present.Distinct().ToArray();
            return Add(1, 1 - yes.Length, yes.Select(i => (i, 1f)).Concat(absent.Distinct().Select(i => (i, -1f))), label);
        }
    }

    private readonly record struct Side(bool Own)
    {
        private int At(int square) => Own ? square : 31 - square;
        public int Man(int square) => (Own ? OwnMan : EnemyMan) * 32 + At(square);
        public int King(int square) => (Own ? OwnKing : EnemyKing) * 32 + At(square);
        public int FoeMan(int square) => (Own ? EnemyMan : OwnMan) * 32 + At(square);
        public int FoeKing(int square) => (Own ? EnemyKing : OwnKing) * 32 + At(square);
        public int Free(int square) => Empty * 32 + At(square);
    }

    private sealed class SideFeatures
    {
        public readonly List<int> Steps = [], Captures = [];
        public readonly List<(int Node, float Scale)> Exposures = [];
    }

    public static NetworkDefinition Build()
    {
        var graph = new Graph();
        var own = BuildSide(graph, new Side(true), [35f, 70f]);
        var enemy = BuildSide(graph, new Side(false), [90f, 170f]);
        var output = new List<(int, float)>();

        foreach (var (side, sign) in new[] { (new Side(true), 1f), (new Side(false), -1f) })
        {
            for (var square = 0; square < 32; square++)
            {
                int row = Row(square), column = Column(square), rank = 7 - row;
                int centre = 7 - Math.Max(Math.Abs(2 * row - 7), Math.Abs(2 * column - 7));
                output.Add((side.Man(square), sign * 100));
                output.Add((side.King(square), sign * 300));
                output.Add((side.Man(square), sign * Advance[rank]));
                output.Add((side.Man(square), sign * centre * 1.5f));
                output.Add((side.King(square), sign * centre * 2));
                if (rank == 0) output.Add((side.Man(square), sign * 8));
                foreach (var direction in Forward)
                {
                    var behind = Step(square, direction + 2);
                    if (behind < 0) continue;
                    output.Add((graph.Condition([side.Man(square), side.Man(behind)], []), sign * 3));
                    output.Add((graph.Condition([side.Man(square), side.King(behind)], []), sign * 3));
                }
            }
        }

        AddExposure(graph, output, own.Exposures, -1, "waiting");
        AddExposure(graph, output, enemy.Exposures, 1, "toMove");
        var noMoves = graph.Add(3, 1, own.Steps.Concat(enemy.Captures).Select(n => (n, -1f)), "own:noMoves");
        output.Add((noMoves, -20000));

        var chooserMaterial = new List<(int From, float Weight)>();
        for (var square = 0; square < 32; square++)
        {
            chooserMaterial.Add((new Side(true).Man(square), -1f));
            chooserMaterial.Add((new Side(true).King(square), -3f));
            chooserMaterial.Add((new Side(false).Man(square), 1f));
            chooserMaterial.Add((new Side(false).King(square), 3f));
        }
        var aboveZero = graph.Add(2, 0, chooserMaterial, "chooser:materialAboveZero");
        var aboveOne = graph.Add(3, -1, chooserMaterial, "chooser:materialAboveOne");
        var materialGate = graph.Add(4, 0, [(aboveZero, 1f), (aboveOne, -1f)], "chooser:materialAhead");
        var kingReturn = graph.Add(5, -1, [(Inputs - 1, 1f), (materialGate, 1f)], "chooser:quietKingReturn");
        output.Add((kingReturn, 5f));
        graph.Add(6, 0, output, "output");
        return new NetworkDefinition(Network.FormatName, "Klinamite v3",
            "Pesi manuali: rete v2 invariata e ritorno quieto immediato di dama penalizzato di 5 solo con vantaggio materiale del giocatore che sceglie. Nessun training.", Inputs, [.. graph.Neurons]);
    }

    private static SideFeatures BuildSide(Graph graph, Side victimSide, float[] values)
    {
        var features = new SideFeatures();
        var attacks = new List<int>[32, 2];
        for (var square = 0; square < 32; square++)
            for (var type = 0; type < 2; type++) attacks[square, type] = [];

        for (var start = 0; start < 32; start++)
        {
            foreach (var direction in All)
            {
                var adjacent = Step(start, direction);
                if (adjacent < 0) continue;
                if (victimSide.Own)
                {
                    if (direction < 2)
                        features.Steps.Add(graph.Condition([victimSide.Man(start), victimSide.Free(adjacent)], [], "man-step"));
                    features.Steps.Add(graph.Condition([victimSide.King(start), victimSide.Free(adjacent)], [], "king-step"));
                }

                var landing = Jump(start, direction);
                if (landing < 0) continue;
                if (Down.Contains(direction))
                {
                    var capture = graph.Condition([victimSide.FoeMan(start), victimSide.Man(adjacent), victimSide.Free(landing)], [], "man-attacks-man");
                    attacks[adjacent, 0].Add(capture);
                    features.Captures.Add(capture);
                }
                foreach (var victimType in new[] { 0, 1 })
                {
                    var victim = victimType == 0 ? victimSide.Man(adjacent) : victimSide.King(adjacent);
                    var capture = graph.Condition([victimSide.FoeKing(start), victim, victimSide.Free(landing)], [], "king-attacks");
                    attacks[adjacent, victimType].Add(capture);
                    features.Captures.Add(capture);
                }

                foreach (var second in All)
                {
                    var secondVictim = Step(landing, second);
                    var secondLanding = Jump(landing, second);
                    if (secondVictim < 0 || secondLanding < 0 || secondVictim == adjacent || secondLanding == adjacent) continue;
                    var freeLandings = secondLanding == start ? [victimSide.Free(landing)] : new[] { victimSide.Free(landing), victimSide.Free(secondLanding) };
                    if (Down.Contains(direction) && Down.Contains(second))
                    {
                        var path = graph.Condition([victimSide.FoeMan(start), victimSide.Man(adjacent), victimSide.Man(secondVictim), .. freeLandings], [], "man-double");
                        features.Exposures.Add((path, 2 * values[0]));
                    }
                    for (var firstType = 0; firstType < 2; firstType++)
                    for (var secondType = 0; secondType < 2; secondType++)
                    {
                        var first = firstType == 0 ? victimSide.Man(adjacent) : victimSide.King(adjacent);
                        var secondPiece = secondType == 0 ? victimSide.Man(secondVictim) : victimSide.King(secondVictim);
                        var path = graph.Condition([victimSide.FoeKing(start), first, secondPiece, .. freeLandings], [], "king-double");
                        features.Exposures.Add((path, values[firstType] + values[secondType]));
                    }

                    foreach (var third in All)
                    {
                        var thirdVictim = Step(secondLanding, third);
                        var thirdLanding = Jump(secondLanding, third);
                        if (thirdVictim < 0 || thirdLanding < 0 || thirdVictim == adjacent || thirdVictim == secondVictim ||
                            thirdLanding == adjacent || thirdLanding == secondVictim) continue;
                        var tripleLandings = new List<int> { victimSide.Free(landing) };
                        foreach (var square in new[] { secondLanding, thirdLanding })
                            if (square != start && !tripleLandings.Contains(victimSide.Free(square)))
                                tripleLandings.Add(victimSide.Free(square));
                        if (Down.Contains(direction) && Down.Contains(second) && Down.Contains(third))
                        {
                            var path = graph.Condition([victimSide.FoeMan(start), victimSide.Man(adjacent), victimSide.Man(secondVictim), victimSide.Man(thirdVictim), .. tripleLandings], [], "man-triple");
                            features.Exposures.Add((path, 3 * values[0]));
                        }
                        for (var firstType = 0; firstType < 2; firstType++)
                        for (var secondType = 0; secondType < 2; secondType++)
                        for (var thirdType = 0; thirdType < 2; thirdType++)
                        {
                            var first = firstType == 0 ? victimSide.Man(adjacent) : victimSide.King(adjacent);
                            var secondPiece = secondType == 0 ? victimSide.Man(secondVictim) : victimSide.King(secondVictim);
                            var thirdPiece = thirdType == 0 ? victimSide.Man(thirdVictim) : victimSide.King(thirdVictim);
                            var path = graph.Condition([victimSide.FoeKing(start), first, secondPiece, thirdPiece, .. tripleLandings], [], "king-triple");
                            features.Exposures.Add((path, values[firstType] + values[secondType] + values[thirdType]));
                        }
                    }
                }
            }
        }

        for (var square = 0; square < 32; square++)
            for (var type = 0; type < 2; type++)
                if (attacks[square, type].Count > 0)
                    features.Exposures.Add((SaturatingOr(graph, attacks[square, type], $"victim:{square}:{type}"), values[type]));
        return features;
    }

    private static int SaturatingOr(Graph graph, List<int> detectors, string label)
    {
        int sum = graph.Add(2, 0, detectors.Select(n => (n, 1f)));
        int aboveOne = graph.Add(2, -1, detectors.Select(n => (n, 1f)));
        return graph.Add(3, 0, [(sum, 1), (aboveOne, -1)], label);
    }

    private static void AddExposure(Graph graph, List<(int, float)> output, List<(int Node, float Scale)> exposures, float sign, string name)
    {
        output.Add((Maximum(graph, exposures, name), sign));
    }

    private static int Maximum(Graph graph, List<(int Node, float Scale)> terms, string label)
    {
        var current = graph.Add(4, 0, [terms[0]]);
        foreach (var term in terms.Skip(1))
        {
            var difference = graph.Add(4, 0, [(current, 1), (term.Node, -term.Scale)]);
            current = graph.Add(4, 0, [(term.Node, term.Scale), (difference, 1)]);
        }
        graph.Neurons[current - Inputs] = graph.Neurons[current - Inputs] with { Label = label };
        return current;
    }
}
