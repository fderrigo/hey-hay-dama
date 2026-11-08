namespace WeightBuilder;

public sealed record NeuronDefinition(int Layer, float Bias, int[] From, float[] Weights, string? Label = null);
public sealed record NetworkDefinition(string Format, string Name, string Construction, int Inputs, NeuronDefinition[] Neurons);

/// <summary>
/// Writes the weights by construction. No game data, gradient or search over values is involved:
/// each neuron implements a stated logical condition and each output weight is a value chosen by
/// reasoning about draughts. Running this twice gives the same file.
///
/// Inputs   5 planes of 32 squares, seen by the side to move, which always moves up.
/// Layer 1  conditions on squares: one ReLU unit is an AND of present and absent literals.
/// Layer 2  safe captures: a capture with no recapture.
/// Layer 3  channels: weighted sums of conditions, one set per side.
/// Layer 4  saturating "at least one" detectors, game phases, "no legal move".
/// Layer 5  phase gates: a channel passes only in its phase.
/// Output   linear combination of the gates and detectors.
/// </summary>
public static class NetworkBuilder
{
    private const int Inputs = 5 * 32, OwnMan = 0, OwnKing = 1, EnemyMan = 2, EnemyKing = 3, Empty = 4;
    private const int UpLeft = 0, UpRight = 1, DownLeft = 2, DownRight = 3;
    private const float GateRange = 1000f, NoMovesValue = 20000f;
    private static readonly int[] Forward = [UpLeft, UpRight];
    private static readonly int[] All = [UpLeft, UpRight, DownLeft, DownRight];
    private static readonly int[] Advance = [0, 1, 2, 4, 7, 12, 20];

    // Hundredths of a man per channel unit, by phase: opening (17+ pieces), middle (9-16), end (up to 8).
    private static readonly (string Channel, float Open, float Middle, float End)[] Values =
    [
        ("men", 100, 100, 110),
        ("kings", 150, 165, 190),
        ("advance", 0.8f, 1, 2.2f),
        ("centre", 1, 1, 0),
        ("guard", 1, 1, 0.2f),
        ("edge", -1, -1, 0),
        ("support", 3, 3, 1),
        ("blocked", -2, -2, -2),
        ("runner", 0, 4, 8),
        ("mobility", 1.5f, 1.5f, 2.5f),
        ("kingCentre", 2, 2, 2),
    ];

    // v8: a man next to promotion must be worth less than the king it becomes. In v7 advance and runner summed to
    // 108 at the end, more than a king minus a man (80), so promoting lost value. Same table otherwise.
    private static readonly (string Channel, float Open, float Middle, float End)[] ValuesV8 =
    [
        .. Values.Select(v => v.Channel switch
        {
            "advance" => v with { End = 1f },
            "runner" => v with { Middle = 2, End = 3.5f },
            _ => v,
        }),
    ];

    // The side to move must capture now; the other side only threatens, and may be answered.
    private static readonly (string Channel, float ToMove, float Waiting)[] Captures =
    [
        ("capture1", 40, 15),
        ("safeCapture", 60, 20),
        ("capture2", 200, 70),
        ("capture3", 300, 100),
    ];

    // v3: capture3 implies capture2 implies capture1, so these are increments and the sum is the longest path,
    // not a total over alternatives. Each extra man is worth one man to the side to move; a waiting side may parry.
    private static readonly (string Channel, float ToMove, float Waiting)[] CapturesV3 =
    [
        ("capture1", 40, 15),
        ("safeCapture", 60, 20),
        ("capture2", 100, 35),
        ("capture3", 100, 35),
    ];

    // v9: capture1 saturates, so a side that already had any capture paid nothing for offering a king or a promotion
    // on top of it. Roughly a king minus a man, for the side to move; a threat is mostly answered, so only the 15 of capture1.
    private const float PricedToMove = 70, PricedWaiting = 15;

    // v3: the side to move is about to win one man and lose two: cancels the +100 of a safe capture, then one man more.
    private const float TrapValue = -200;

    // v7: cancels the +40 of a capture that will be answered, then a quarter of a man for the side that wants the trade.
    private const float TradeOfferedValue = -65;

    // v6: ahead, each enemy piece gone below SimplifyFrom is worth a quarter of a man. A one-for-one trade then beats
    // a quiet move (a few hundredths), but no sacrifice of a man pays for it.
    private const float SimplifyValue = 25, SimplifyFrom = 5;

    // v4: one man won, two lost through the landing square. No safe bonus to cancel: only the 40 of a plain capture.
    private const float LandingTrapValue = -140;

    // v4: a man one step from promotion gains about a king minus a man, if nothing forces the side elsewhere.
    private const float PromoteToMove = 50, PromoteWaiting = 15;

    private static int Row(int sq) => sq / 4;
    private static int Column(int sq) => sq / 4 % 2 + 2 * (sq % 4);
    private static int Square(int row, int column) => row is >= 0 and < 8 && column is >= 0 and < 8 && (row + column) % 2 == 0 ? row * 4 + column / 2 : -1;
    private static int Step(int sq, int d) => Square(Row(sq) + (d < 2 ? -1 : 1), Column(sq) + (d % 2 == 0 ? -1 : 1));
    private static int Jump(int sq, int d) => Square(Row(sq) + (d < 2 ? -2 : 2), Column(sq) + (d % 2 == 0 ? -2 : 2));

    private sealed class Graph
    {
        public readonly List<NeuronDefinition> Neurons = [];

        public int Add(int layer, float bias, IEnumerable<(int From, float Weight)> links, string? label = null)
        {
            var merged = links.GroupBy(l => l.From).Select(g => (From: g.Key, Weight: g.Sum(l => l.Weight))).OrderBy(l => l.From).ToArray();
            Neurons.Add(new NeuronDefinition(layer, bias, merged.Select(l => l.From).ToArray(), merged.Select(l => l.Weight).ToArray(), label));
            return Inputs + Neurons.Count - 1;
        }

        /// <summary>Outputs 1 exactly when every present literal is 1 and every absent one is 0.</summary>
        public int Condition(IEnumerable<int> present, IEnumerable<int> absent, int layer = 1)
        {
            var yes = present.Distinct().ToArray();
            return Add(layer, 1 - yes.Length, yes.Select(i => (i, 1f)).Concat(absent.Distinct().Select(i => (i, -1f))));
        }
    }

    /// <summary>Literals for one side. Squares are relative: that side always moves up.</summary>
    private readonly record struct Side(bool Own)
    {
        private int At(int r) => Own ? r : 31 - r;
        public int Man(int r) => (Own ? OwnMan : EnemyMan) * 32 + At(r);
        public int King(int r) => (Own ? OwnKing : EnemyKing) * 32 + At(r);
        public int FoeMan(int r) => (Own ? EnemyMan : OwnMan) * 32 + At(r);
        public int FoeKing(int r) => (Own ? EnemyKing : OwnKing) * 32 + At(r);
        public int Free(int r) => Empty * 32 + At(r);
    }

    private sealed record CapturePath(int Start, bool King, int[] Landings, int[] Victims);

    private sealed class Channels
    {
        private readonly Dictionary<string, List<(int, float)>> links = [];
        public List<(int, float)> this[string name] => links.TryGetValue(name, out var list) ? list : links[name] = [];
    }

    public static NetworkDefinition Build() => Build(1);
    public static NetworkDefinition BuildV3() => Build(3);
    public static NetworkDefinition BuildV4(bool landing = true, bool promote = true) => Build(4, landing, promote);
    public static NetworkDefinition BuildV5() => Build(5, false, false);
    public static NetworkDefinition BuildV6() => Build(3, simplify: true);
    public static NetworkDefinition BuildV7() => Build(3, exchange: true);
    public static NetworkDefinition BuildV8() => Build(3, exchange: true, promotion: true);
    public static NetworkDefinition BuildV9() => Build(3, exchange: true, promotion: true, priced: true);

    private static NetworkDefinition Build(int version, bool landing = true, bool promote = true, bool simplify = false, bool exchange = false, bool promotion = false, bool priced = false)
    {
        var v3 = version >= 3;
        var graph = new Graph();
        Channels own = BuildSide(graph, new Side(true), version, priced), enemy = BuildSide(graph, new Side(false), version, priced);

        // Phase comes from a count of the pieces, turned into exact 0/1 signals with two units each.
        var pieces = Enumerable.Range(0, 32 * 4).Select(i => (i, 1f)).ToArray();
        int above8 = graph.Add(1, -8, pieces), above9 = graph.Add(1, -9, pieces);
        int above16 = graph.Add(1, -16, pieces), above17 = graph.Add(1, -17, pieces);
        var atLeast9 = graph.Add(2, 0, [(above8, 1), (above9, -1)]);
        var atLeast17 = graph.Add(2, 0, [(above16, 1), (above17, -1)]);
        var opening = graph.Add(4, 0, [(atLeast17, 1)], "phase:opening");
        var middle = graph.Add(4, 0, [(atLeast9, 1), (atLeast17, -1)], "phase:middle");
        var ending = graph.Add(4, 1, [(atLeast9, -1)], "phase:end");

        var output = new List<(int, float)>();
        int anyCapture = -1, anySafe = -1;
        foreach (var (channels, name, sign) in new[] { (own, "own", 1f), (enemy, "enemy", -1f) })
        {
            foreach (var (channel, open, mid, end) in promotion ? ValuesV8 : Values)
            {
                var sum = graph.Add(3, 0, channels[channel], $"{name}:{channel}");
                foreach (var (phase, value) in new[] { (opening, open), (middle, mid), (ending, end) })
                    if (value != 0) output.Add((graph.Add(5, -GateRange, [(sum, 1), (phase, GateRange)]), sign * value));
            }
            foreach (var (channel, toMove, waiting) in v3 ? CapturesV3 : Captures)
            {
                var any = AtLeastOne(graph, channels[channel], $"{name}:{channel}");
                if (sign > 0 && channel == "capture1") anyCapture = any;
                if (sign > 0 && channel == "safeCapture") anySafe = any;
                output.Add((any, sign * (sign > 0 ? toMove : waiting)));
            }
            if (priced)
                foreach (var channel in new[] { "captureKing", "capturePromote" })
                    output.Add((AtLeastOne(graph, channels[channel], $"{name}:{channel}"), sign * (sign > 0 ? PricedToMove : PricedWaiting)));
            if (version >= 4 && promote)
                output.Add((AtLeastOne(graph, channels["promote"], $"{name}:promote"), sign * (sign > 0 ? PromoteToMove : PromoteWaiting)));
        }

        if (v3) output.Add((AtLeastOne(graph, own["trap"], "own:trap"), TrapValue));
        if (exchange)
        {
            // Every capture of the side to move is answered, and the other side leads: an offered trade, good for the leader.
            var answered = graph.Add(5, 0, [(anyCapture, 1), (anySafe, -1)]);
            var theyLead = Ahead(graph, EnemyMan, EnemyKing, OwnMan, OwnKing);
            output.Add((graph.Add(5, -1, [(answered, 1), (theyLead, 1)], "own:tradeOffered"), TradeOfferedValue));
        }
        if (simplify)
        {
            output.Add((Simplify(graph, OwnMan, OwnKing, EnemyMan, EnemyKing, "own:simplify"), SimplifyValue));
            output.Add((Simplify(graph, EnemyMan, EnemyKing, OwnMan, OwnKing, "enemy:simplify"), -SimplifyValue));
        }
        if (version >= 4 && landing) output.Add((AtLeastOne(graph, own["landingTrap"], "own:landingTrap"), LandingTrapValue));

        // With no step and no jump the side to move has lost, whether blocked or out of pieces.
        var moves = own["steps"].Concat(own["capture1"]);
        output.Add((graph.Add(4, 1, moves.Select(l => (l.Item1, -1f)), "own:noMoves"), -NoMovesValue));

        graph.Add(6, 0, output, "output");
        return new NetworkDefinition("agata-net/v1", priced ? "agata-v9" : promotion ? "agata-v8" : exchange ? "agata-v7" : simplify ? "agata-v6" : $"agata-v{version}",
            "Pesi scritti per costruzione da NetworkBuilder, senza training, fine-tuning o dati di partite.", Inputs, [.. graph.Neurons]);
    }

    private static (int, float)[] Plane(int plane, float weight) => [.. Enumerable.Range(0, 32).Select(i => (plane * 32 + i, weight))];

    // 1 when these planes hold more pieces than the foe's, else 0.
    private static int Ahead(Graph graph, int man, int king, int foeMan, int foeKing)
    {
        (int, float)[] difference = [.. Plane(man, 1), .. Plane(king, 1), .. Plane(foeMan, -1), .. Plane(foeKing, -1)];
        int lead = graph.Add(1, 0, difference), leadBeyondOne = graph.Add(1, -1, difference);
        return graph.Add(2, 0, [(lead, 1), (leadBeyondOne, -1)]);
    }

    // With more pieces than the enemy: how far the enemy is below SimplifyFrom pieces, else 0.
    private static int Simplify(Graph graph, int man, int king, int foeMan, int foeKing, string label)
    {
        var ahead = Ahead(graph, man, king, foeMan, foeKing);
        var few = graph.Add(1, SimplifyFrom, [.. Plane(foeMan, -1), .. Plane(foeKing, -1)]);
        return graph.Add(3, -SimplifyFrom, [(few, 1), (ahead, SimplifyFrom)], label);
    }

    // min(1, sum), from three ReLU units: a saturating OR.
    private static int AtLeastOne(Graph graph, List<(int, float)> links, string label)
    {
        int first = graph.Add(3, 0, links), second = graph.Add(3, -1, links);
        return graph.Add(4, 0, [(first, 1), (second, -1)], label);
    }

    private static Channels BuildSide(Graph graph, Side side, int version, bool priced)
    {
        var c = new Channels();
        for (var r = 0; r < 32; r++)
        {
            int row = Row(r), column = Column(r), rank = 7 - row;
            int man = side.Man(r), king = side.King(r);
            c["men"].Add((man, 1));
            c["kings"].Add((king, 1));
            c["kingCentre"].Add((king, 7 - Math.Max(Math.Abs(2 * row - 7), Math.Abs(2 * column - 7))));

            foreach (var d in All)
                if (Step(r, d) is >= 0 and var to) AddStep(c, graph.Condition([king, side.Free(to)], []));

            if (rank < 7) // A man never stands on the row where it would have promoted.
            {
                if (version >= 4 && rank == 6)
                    foreach (var d in Forward)
                        if (Step(r, d) is >= 0 and var crown) c["promote"].Add((graph.Condition([man, side.Free(crown)], []), 1));
                c["advance"].Add((man, Advance[rank]));
                if (r is 13 or 14 or 17 or 18) c["centre"].Add((man, 6));      // D5 F5 C4 E4.
                else if (r is 9 or 10 or 21 or 22) c["centre"].Add((man, 3));  // C6 E6 D3 F3.
                if (r is 29 or 30) c["guard"].Add((man, 8));                   // D1 F1 guard promotion best.
                else if (r is 28 or 31) c["guard"].Add((man, 4));
                if (column is 0 or 7) c["edge"].Add((man, 4));                 // Attacks in one direction only.

                int[] ahead = [.. Forward.Select(d => Step(r, d)).Where(s => s >= 0)];
                int[] behind = [.. new[] { DownLeft, DownRight }.Select(d => Step(r, d)).Where(s => s >= 0)];
                // A rear square held by our own piece: neither empty nor an enemy.
                c["support"].Add((graph.Condition([man], behind.SelectMany(b => new[] { side.Free(b), side.FoeMan(b), side.FoeKing(b) })), 1));
                c["blocked"].Add((graph.Condition([man], ahead.Select(side.Free)), 1));
                foreach (var a in ahead) AddStep(c, graph.Condition([man, side.Free(a)], []));

                var cone = Enumerable.Range(0, 32).Where(o => row - Row(o) > 0 && Math.Abs(column - Column(o)) <= row - Row(o));
                c["runner"].Add((graph.Condition([man], cone.SelectMany(o => new[] { side.FoeMan(o), side.FoeKing(o) })), 2 + rank));
            }

            foreach (var path in Paths(r, false).Concat(Paths(r, true))) AddCapture(graph, side, c, path, version, priced);
        }
        return c;
    }

    private static void AddStep(Channels c, int neuron)
    {
        c["mobility"].Add((neuron, 1));
        c["steps"].Add((neuron, 1));
    }

    private static void AddCapture(Graph graph, Side side, Channels c, CapturePath path, int version, bool priced)
    {
        var v3 = version >= 3;
        var piece = path.King ? side.King(path.Start) : side.Man(path.Start);
        var free = path.Landings.Where(l => l != path.Start).Select(side.Free).ToArray();
        // A man takes only men. A king takes either: "neither empty nor ours" says that in one neuron.
        var capture = path.King
            ? graph.Condition(free.Append(piece), path.Victims.SelectMany(v => new[] { side.Free(v), side.Man(v), side.King(v) }))
            : graph.Condition(free.Append(piece).Concat(path.Victims.Select(side.FoeMan)), []);

        c[$"capture{path.Victims.Length}"].Add((capture, 1));
        if (priced)
        {
            // Only a king takes a king; a man stops on the last row, so its path ends there.
            if (path.King) foreach (var v in path.Victims) c["captureKing"].Add((graph.Condition([capture, side.FoeKing(v)], [], 2), 1));
            else if (Row(path.Landings[^1]) == 0) c["capturePromote"].Add((capture, 1));
        }
        if (path.Victims.Length == 1)
        {
            var answers = Recaptures(graph, side, path);
            if (v3) answers.AddRange(Vacated(side, path).Select(p => graph.Condition(p, [])));
            c["safeCapture"].Add((graph.Add(2, 0, answers.Select(n => (n, -1f)).Append((capture, 1f))), 1));
            // Only the side to move can be forced into it, so the other side needs no such units.
            if (v3 && side.Own && !path.King)
            {
                // v5: a capture that must carry on is not the single capture the motif describes.
                var single = version >= 5 ? Single(graph, side, path, capture) : capture;
                foreach (var literals in Vacated(side, path))
                    foreach (var d in new[] { DownLeft, DownRight })
                        if (Step(path.Start, d) is >= 0 and var second && Jump(path.Start, d) is >= 0 and var landing)
                            c["trap"].Add((graph.Condition(literals.Append(single).Append(side.Man(second)).Append(side.Free(landing)), [], version >= 5 ? 2 : 1), 1));
            }
            if (version >= 4 && side.Own && !path.King) AddLandingTraps(graph, side, c, path, capture);
        }
    }

    /// <summary>Conditions under which the capturing piece can be taken on its landing square.</summary>
    private static List<int> Recaptures(Graph graph, Side side, CapturePath path)
    {
        var (land, victim) = (path.Landings[0], path.Victims[0]);
        var result = new List<int>();
        foreach (var d in All)
        {
            int attacker = Step(land, d), beyond = Step(land, 3 - d);
            if (attacker < 0 || beyond < 0 || attacker == victim || attacker == path.Start) continue;
            int[] open = beyond == path.Start || beyond == victim ? [] : [side.Free(beyond)];
            // Enemy men move down in relative squares, so they attack from above, and take only men.
            if (!path.King && d is (UpLeft or UpRight)) result.Add(graph.Condition(open.Append(side.FoeMan(attacker)), []));
            result.Add(graph.Condition(open.Append(side.FoeKing(attacker)), []));
        }
        return result;
    }

    /// <summary>1 when this man's capture ends on its landing square: no further man to jump from there.</summary>
    private static int Single(Graph graph, Side side, CapturePath path, int capture)
    {
        var land = path.Landings[0];
        var onward = new List<(int, float)> { (capture, 1) };
        foreach (var d in Forward)
            if (Step(land, d) is >= 0 and var over && Jump(land, d) is >= 0 and var next && over != path.Victims[0])
                onward.Add((graph.Condition([side.FoeMan(over), next == path.Start ? side.Man(path.Start) : side.Free(next)], []), -1));
        return graph.Add(2, 0, onward);
    }

    /// <summary>An enemy man takes the capturing piece on its landing square and carries on over a second man.</summary>
    private static void AddLandingTraps(Graph graph, Side side, Channels c, CapturePath path, int capture)
    {
        var (land, victim) = (path.Landings[0], path.Victims[0]);
        int[] Open(int square) => square == path.Start || square == victim ? [] : [side.Free(square)];
        foreach (var d in Forward)
        {
            int attacker = Step(land, d), stop = Step(land, 3 - d);
            if (attacker < 0 || stop < 0 || attacker == victim) continue;
            foreach (var e in new[] { DownLeft, DownRight })
            {
                int second = Step(stop, e), end = Jump(stop, e);
                if (second < 0 || end < 0 || second == path.Start || second == victim || second == land) continue;
                c["landingTrap"].Add((graph.Condition(Open(stop).Concat(Open(end)).Append(capture).Append(side.FoeMan(attacker)).Append(side.Man(second)), []), 1));
            }
        }
    }

    /// <summary>An enemy man or king that can jump a man of ours into the square the capturing piece just left.</summary>
    private static IEnumerable<int[]> Vacated(Side side, CapturePath path)
    {
        foreach (var d in Forward)
        {
            int neighbour = Step(path.Start, d), attacker = Jump(path.Start, d);
            if (neighbour < 0 || attacker < 0 || neighbour == path.Victims[0] || attacker == path.Landings[0]) continue;
            yield return [side.Man(neighbour), side.FoeMan(attacker)];
            yield return [side.Man(neighbour), side.FoeKing(attacker)];
        }
    }

    private static IEnumerable<CapturePath> Paths(int start, bool king)
    {
        var found = new List<CapturePath>();
        Walk(start, start, king, [], [], found);
        return found.GroupBy(p => string.Join(',', p.Victims) + ">" + string.Join(',', p.Landings)).Select(g => g.First());
    }

    private static void Walk(int start, int at, bool king, List<int> landings, List<int> victims, List<CapturePath> found)
    {
        if (victims.Count == 3) return;
        foreach (var d in king ? All : Forward)
        {
            int over = Step(at, d), land = Jump(at, d);
            if (land < 0 || victims.Contains(over) || over == start) continue;
            landings.Add(land);
            victims.Add(over);
            found.Add(new CapturePath(start, king, [.. landings], [.. victims]));
            Walk(start, land, king, landings, victims, found);
            landings.RemoveAt(landings.Count - 1);
            victims.RemoveAt(victims.Count - 1);
        }
    }
}
