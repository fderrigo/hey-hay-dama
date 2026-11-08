using System.Buffers;
using System.Text.Json;
using Dama.Engine;

namespace Opale.C;

public sealed record Unit(string Label, double Bias, double OutputWeight, int[] From, double[] Weights);
public sealed record Definition(string Format, string Name, string Construction, int Inputs, Unit[] Units);

/// <summary>Only sparse affine transforms and ReLU. No game rules or search inside inference.</summary>
public sealed class Network
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public Definition Definition { get; }
    public int Parameters => Definition.Units.Sum(n => n.Weights.Length + 2);
    public Network(Definition definition)
    {
        if (definition.Format != "opale-c-relu/v1" || definition.Inputs != 128 || definition.Units.Length is < 1 or > 100000)
            throw new InvalidDataException("Invalid Opale.C network.");
        for (int j = 0; j < definition.Units.Length; j++)
        {
            var n = definition.Units[j];
            if (!double.IsFinite(n.Bias) || !double.IsFinite(n.OutputWeight) || n.From.Length != n.Weights.Length || n.Weights.Any(w => !double.IsFinite(w)) || n.From.Any(i => i < 0 || i >= 128 + j))
                throw new InvalidDataException("Invalid or non-feed-forward connection.");
        }
        Definition = definition;
    }

    public static Network Load(string path) => new(JsonSerializer.Deserialize<Definition>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty network."));
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(Definition, Json));

    public double Evaluate(int[] board, int perspective) => Compute(board, perspective, null);
    public double Feature(int[] board, int perspective, string label) => Compute(board, perspective, label);

    private double Compute(int[] board, int perspective, string? feature)
    {
        if (board.Length != 32 || perspective is not (1 or -1)) throw new ArgumentException("Invalid input.");
        var values = ArrayPool<double>.Shared.Rent(128 + Definition.Units.Length);
        try
        {
            Array.Clear(values, 0, 128);
            for (int s = 0; s < 32; s++)
            {
                int piece = board[s] * perspective;
                if (piece == 0) continue;
                int plane = piece switch { 1 => 0, 2 => 1, -1 => 2, -2 => 3, _ => throw new ArgumentException("Invalid piece.") };
                values[32 * plane + (perspective == 1 ? s : 31 - s)] = 1;
            }
            double output = 0;
            for (int j = 0; j < Definition.Units.Length; j++)
            {
                var unit = Definition.Units[j];
                double sum = unit.Bias;
                for (int k = 0; k < unit.From.Length; k++) sum += values[unit.From[k]] * unit.Weights[k];
                double activation = Math.Max(0, sum);
                values[128 + j] = activation;
                output += activation * unit.OutputWeight;
                if (feature == unit.Label) return activation;
            }
            if (feature is not null) throw new ArgumentException("Unknown feature: " + feature);
            return output;
        }
        finally { ArrayPool<double>.Shared.Return(values); }
    }
}

public sealed class Model(Network network)
{
    public Network Network { get; } = network;
    public Decision Choose(Position p, IReadOnlyList<Move> legal, CancellationToken cancellation = default)
    {
        if (legal.Count == 0) throw new ArgumentException("No legal moves.");
        Move best = legal[0]; double bestScore = double.NegativeInfinity;
        foreach (var move in legal)
        {
            cancellation.ThrowIfCancellationRequested();
            var next = Rules.Apply(p, move);
            var outcome = Rules.GetOutcome(next);
            double score = outcome is null ? -Network.Evaluate(next.Board, next.Turn) : outcome.Winner == 0 ? 0 : outcome.Winner * p.Turn * 100000;
            if (score > bestScore) { bestScore = score; best = move; }
        }
        return new Decision(best, bestScore, legal.Count, 1);
    }
}
