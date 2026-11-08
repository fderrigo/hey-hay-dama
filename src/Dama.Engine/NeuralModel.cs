using System.Text.Json;

namespace Dama.Engine;

public sealed record Connection(int Input, double Weight);
public sealed record Neuron(string Feature, double Bias, double OutputWeight, Connection[] Connections);
public sealed record NetworkWeights(string Format, string Name, int InputSize, string Construction, Neuron[] Hidden);

/// <summary>A sparse, feed-forward 128 → ReLU → 1 network. No optimizer, training, or search in inference.</summary>
public sealed class NeuralModel
{
    public NetworkWeights Weights { get; }
    public int ParameterCount => Weights.Hidden.Sum(n => n.Connections.Length + 2);

    public NeuralModel(NetworkWeights weights)
    {
        if (weights.Format != "dama-relu/v1" || weights.InputSize != 128 || weights.Hidden.Length == 0 || weights.Hidden.Length > 100_000)
            throw new InvalidDataException("Formato della rete non valido.");
        foreach (var n in weights.Hidden)
            if (!double.IsFinite(n.Bias) || !double.IsFinite(n.OutputWeight) || n.Connections.Any(c => c.Input is < 0 or >= 128 || !double.IsFinite(c.Weight)))
                throw new InvalidDataException("Pesi della rete non validi.");
        Weights = weights;
    }

    public static NeuralModel Load(string path) => new(JsonSerializer.Deserialize<NetworkWeights>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("File dei pesi vuoto."));
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public double Evaluate(Position position, int perspective)
    {
        Span<double> input = stackalloc double[128];
        input.Clear();
        for (var square = 0; square < 32; square++)
        {
            var piece = position.Board[square] * perspective;
            if (piece == 0) continue;
            var normalized = perspective == 1 ? square : 31 - square;
            var plane = piece switch { 1 => 0, 2 => 1, -1 => 2, _ => 3 };
            input[plane * 32 + normalized] = 1;
        }
        double output = 0;
        foreach (var neuron in Weights.Hidden)
        {
            var sum = neuron.Bias;
            foreach (var c in neuron.Connections) sum += input[c.Input] * c.Weight;
            output += Math.Max(0, sum) * neuron.OutputWeight;
        }
        return output;
    }

    public Decision Choose(Position position, IReadOnlyList<Move> legal)
    {
        if (legal.Count == 0) throw new InvalidOperationException("Nessuna mossa legale.");
        Move best = legal[0];
        double bestScore = double.NegativeInfinity;
        foreach (var move in legal)
        {
            var next = Rules.Apply(position, move);
            var outcome = Rules.GetOutcome(next);
            var score = outcome is { Winner: 0 } ? 0 : outcome is not null ? 100_000 : Evaluate(next, position.Turn);
            if (score > bestScore) { bestScore = score; best = move; }
        }
        return new Decision(best, bestScore, legal.Count, 1);
    }
}

public sealed record Decision(Move Move, double Score, long Nodes, int Depth);

public static class WeightBuilder
{
    public static NetworkWeights Build()
    {
        var neurons = new List<Neuron>();
        void Add(string feature, double output, double bias, params Connection[] links) => neurons.Add(new Neuron(feature, bias, output, links));
        // Each motif is mirrored, with opposite output weight, so colour and rotation are symmetric.
        for (var side = 0; side < 2; side++)
        {
            var sign = side == 0 ? 1 : -1;
            var own = side * 64;
            var enemy = (1 - side) * 64;
            var forward = side == 0 ? -1 : 1;
            for (var s = 0; s < 32; s++)
            {
                var (r, c) = Rules.Coordinates(s);
                var advancement = side == 0 ? 7 - r : r;
                var center = (3.5 - Math.Abs(3.5 - c)) + (3.5 - Math.Abs(3.5 - r));
                Add("material-man", sign * 100, 0, new Connection(own + s, 1));
                Add("material-king", sign * 310, 0, new Connection(own + 32 + s, 1));
                Add("advancement", sign * (advancement * advancement * .9), 0, new Connection(own + s, 1));
                Add("center-man", sign * center * 2.2, 0, new Connection(own + s, 1));
                Add("center-king", sign * center * 3.5, 0, new Connection(own + 32 + s, 1));
                if (advancement == 0) Add("back-rank-guard", sign * 11, 0, new Connection(own + s, 1));

                foreach (var dr in new[] { -1, 1 })
                foreach (var dc in new[] { -1, 1 })
                {
                    var adjacent = Rules.Square(r + dr, c + dc);
                    if (adjacent < 0) continue;
                    var empty = Enumerable.Range(0, 4).Select(p => new Connection(p * 32 + adjacent, -1)).ToArray();
                    if (dr == forward) Add("man-mobility", sign * 4, 0, [new(own + s, 1), ..empty]);
                    Add("king-mobility", sign * 5, 0, [new(own + 32 + s, 1), ..empty]);
                    if (dr == -forward)
                        Add("support", sign * 7, -1, new(own + s, 1), new(own + adjacent, 1), new(own + 32 + adjacent, 1));

                    var landing = Rules.Square(r + dr * 2, c + dc * 2);
                    if (landing < 0) continue;
                    var landingEmpty = Enumerable.Range(0, 4).Select(p => new Connection(p * 32 + landing, -1)).ToArray();
                    // ReLU(own + enemy - occupiedLanding - 1) detects an immediate legal jump motif.
                    if (dr == forward)
                        Add("man-capture", sign * 30, -1, [new(own + s, 1), new(enemy + adjacent, 1), ..landingEmpty]);
                    Add("king-capture-man", sign * 30, -1, [new(own + 32 + s, 1), new(enemy + adjacent, 1), ..landingEmpty]);
                    Add("king-capture-king", sign * 70, -1, [new(own + 32 + s, 1), new(enemy + 32 + adjacent, 1), ..landingEmpty]);
                }
            }
        }
        return new NetworkWeights("dama-relu/v1", "Opale v1", 128,
            "Deterministic symbolic construction; signed material, advancement, centre, support, mobility and jump motifs. No training or fine-tuning.", neurons.ToArray());
    }
}
