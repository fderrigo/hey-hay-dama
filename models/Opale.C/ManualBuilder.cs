using Dama.Engine;

namespace Opale.C;

/// <summary>Deterministic symbolic construction. No dataset, game outcomes, optimizer or parameter sweep.</summary>
public static class ManualBuilder
{
    // Manual choice fixed before matches: near-material immediate capture, discounted deferrable threat.
    public const double Immediate = .90, Waiting = .15;
    private sealed record Capture(int Node, int Length, bool King, double Material, bool Promotes);
    private sealed class Graph
    {
        public List<Unit> Units { get; } = [];
        public int Add(string label, double bias, double output, params (int From, double Weight)[] links)
        {
            var merged = links.GroupBy(x => x.From).Select(g => (From: g.Key, Weight: g.Sum(x => x.Weight))).Where(x => x.Weight != 0).OrderBy(x => x.From).ToArray();
            Units.Add(new Unit(label, bias, output, merged.Select(x => x.From).ToArray(), merged.Select(x => x.Weight).ToArray()));
            return 127 + Units.Count;
        }
        public int Condition(string label, IEnumerable<int> present, IEnumerable<int> emptySquares)
        {
            var yes = present.Distinct().ToArray();
            var links = yes.Select(i => (i, 1d)).Concat(emptySquares.Distinct().SelectMany(s => Enumerable.Range(0, 4).Select(plane => (plane * 32 + s, -1d)))).ToArray();
            return Add(label, 1 - yes.Length, 0, links);
        }
        public int Any(IEnumerable<int> nodes, string label)
        {
            var links = nodes.Select(n => (n, 1d)).ToArray();
            int a = Add(label + ":sum", 0, 0, links), b = Add(label + ":excess", -1, 0, links);
            return Add(label, 0, 0, (a, 1), (b, -1));
        }
    }

    public static Definition Build()
    {
        var g = new Graph();
        // Retain the original manually specified material/positional channels, not prior learned files.
        foreach (var n in WeightBuilder.Build().Hidden.Where(n => !n.Feature.Contains("capture", StringComparison.Ordinal)))
            g.Add("base:" + n.Feature, n.Bias, n.OutputWeight, n.Connections.Select(c => (c.Input, c.Weight)).ToArray());
        for (int side = 0; side < 2; side++) BuildCaptures(g, side);
        return new Definition("opale-c-relu/v1", "Opale C manual v3",
            "Hand-built ReLU graph. Original positional channels; turn-asymmetric maximum of legal-geometry capture paths of length <=2, with length and capturing-king gates, plus promotion gain 310-100 on eligible man paths. No training, optimization, search or game memory.", 128, g.Units.ToArray());
    }

    private static void BuildCaptures(Graph g, int side)
    {
        var captures = new List<Capture>();
        int own = side * 64, foe = (1 - side) * 64, forward = side == 0 ? -1 : 1;
        int serial = 0;
        for (int start = 0; start < 32; start++)
        foreach (bool king in new[] { false, true })
        {
            int[] rows = king ? [-1, 1] : [forward];
            foreach (int dr in rows)
            foreach (int dc in new[] { -1, 1 })
            {
                var (r, c) = Rules.Coordinates(start);
                int over = Rules.Square(r + dr, c + dc), land = Rules.Square(r + dr * 2, c + dc * 2);
                if (over < 0 || land < 0) continue;
                foreach (int rank in king ? new[] { 1, 2 } : new[] { 1 })
                {
                    int piece = own + (king ? 32 : 0) + start;
                    int victim = foe + (rank == 2 ? 32 : 0) + over;
                    double value = rank == 2 ? 310 : 100;
                    int node = g.Condition($"path:{side}:{serial++}", [piece, victim], [land]);
                    var (lr, lc) = Rules.Coordinates(land);
                    captures.Add(new Capture(node, 1, king, value, !king && lr == (side == 0 ? 0 : 7)));
                    if (!king && lr == (side == 0 ? 0 : 7)) continue; // Promotion ends a capture turn.
                    foreach (int dr2 in rows)
                    foreach (int dc2 in new[] { -1, 1 })
                    {
                        int over2 = Rules.Square(lr + dr2, lc + dc2), land2 = Rules.Square(lr + dr2 * 2, lc + dc2 * 2);
                        if (over2 < 0 || land2 < 0 || over2 == over || over2 == start || land2 == over || land2 == over2) continue;
                        foreach (int rank2 in king ? new[] { 1, 2 } : new[] { 1 })
                        {
                            int victim2 = foe + (rank2 == 2 ? 32 : 0) + over2;
                            int two = g.Condition($"path:{side}:{serial++}", [piece, victim, victim2], land2 == start ? [land] : [land, land2]);
                            captures.Add(new Capture(two, 2, king, value + (rank2 == 2 ? 310 : 100), !king && Rules.Coordinates(land2).Row == (side == 0 ? 0 : 7)));
                        }
                    }
                }
            }
        }
        int anyDouble = g.Any(captures.Where(p => p.Length == 2).Select(p => p.Node), $"has-double:{side}");
        var lengthFiltered = captures.Select(p => p.Length == 2 ? p : p with { Node = g.Add("length-gate", 0, 0, (p.Node, 1), (anyDouble, -1)) }).ToArray();
        int anyKing = g.Any(lengthFiltered.Where(p => p.King).Select(p => p.Node), $"has-king-capture:{side}");
        int maxNode = g.Add("zero", 0, 0);
        var promotions = new List<int>();
        foreach (var path in lengthFiltered)
        {
            int allowed = path.King ? path.Node : g.Add("king-priority-gate", 0, 0, (path.Node, 1), (anyKing, -1));
            if (path.Promotes) promotions.Add(allowed);
            // max(a,b) = ReLU(a-b) + b; alternatives never sum their separate material gains.
            int difference = g.Add("max-difference", 0, 0, (maxNode, 1), (allowed, -path.Material));
            maxNode = g.Add("max-merge", 0, 0, (difference, 1), (allowed, path.Material));
        }
        g.Add($"capture-max:{side}", 0, side == 0 ? Immediate : -Waiting, (maxNode, 1));
        // All eligible man paths have equal length and capture only men. Thus their
        // material gain is identical; OR promotion adds the gain once, never per alternative.
        int promotion = g.Any(promotions, $"capture-promotes:{side}");
        g.Add($"capture-promotion-gain:{side}", 0, (side == 0 ? Immediate : -Waiting) * (310 - 100), (promotion, 1));
    }
}
