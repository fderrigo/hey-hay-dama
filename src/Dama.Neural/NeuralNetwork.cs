using System.Text.Json;
using Dama.Engine;

namespace Dama.Neural;

public sealed record Weight(int Input, double Value);
public sealed record Neuron(string Feature, double Bias, double OutputWeight, Weight[] Weights);
public sealed record NetworkWeights(string Format, string Name, int InputSize, string Construction, Neuron[] Hidden);

public sealed class NeuralNetwork
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public NetworkWeights Weights { get; }
    public int ParameterCount => Weights.Hidden.Sum(neuron => neuron.Weights.Length + 2);

    public NeuralNetwork(NetworkWeights weights)
    {
        if (weights.Format != "kimberlite-relu/v1" || weights.InputSize != 128 || weights.Hidden.Length < 128 || weights.Hidden.Length > 10_000)
            throw new InvalidDataException("Formato della rete Kimberlite non valido.");
        foreach (var neuron in weights.Hidden)
        {
            if (!double.IsFinite(neuron.Bias) || !double.IsFinite(neuron.OutputWeight) || neuron.Weights.Length == 0)
                throw new InvalidDataException("Pesi della rete Kimberlite non validi.");
            foreach (var weight in neuron.Weights)
                if (weight.Input is < 0 or >= 128 || !double.IsFinite(weight.Value))
                    throw new InvalidDataException("Connessione della rete Kimberlite non valida.");
        }
        Weights = weights;
    }

    public static NeuralNetwork Load(string path) => new(
        JsonSerializer.Deserialize<NetworkWeights>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException("File dei pesi Kimberlite vuoto."));

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
            var activation = neuron.Bias;
            foreach (var weight in neuron.Weights) activation += input[weight.Input] * weight.Value;
            output += Math.Max(0, activation) * neuron.OutputWeight;
        }
        return output;
    }
}

public sealed class KimberliteModel
{
    public NeuralNetwork Network { get; }

    public KimberliteModel() : this(new NeuralNetwork(KimberliteWeightBuilder.Build())) { }
    public KimberliteModel(string weightsPath) : this(NeuralNetwork.Load(weightsPath)) { }
    public KimberliteModel(NeuralNetwork network) => Network = network;

    public KimberliteDecision Choose(KimberliteRequest request, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var turn = request.Player == "white" ? 1 : -1;
        var position = new Position((int[])request.Position.Board.Clone(), turn, request.Position.Ply, request.Position.QuietKingPlyCount);
        var legal = request.LegalMoves.Select(move => new Move(
            move.Path.Select(square => square - 1).ToArray(),
            move.Captures.Select(square => square - 1).ToArray(),
            position.Board[move.From - 1],
            move.Captures.Select(square => Math.Abs(position.Board[square - 1])).ToArray(),
            move.Promotes)).ToArray();

        Move best = legal[0];
        var bestScore = double.NegativeInfinity;
        foreach (var move in legal)
        {
            cancellation.ThrowIfCancellationRequested();
            var next = Rules.Apply(position, move);
            var outcome = Rules.GetOutcome(next, legal: Rules.LegalMoves(next));
            var score = outcome is { Winner: 0 } ? 0 : outcome is not null ? 100_000 : Network.Evaluate(next, turn);
            if (score > bestScore)
            {
                best = move;
                bestScore = score;
            }
        }
        return new KimberliteDecision(best.Id, bestScore, legal.Length);
    }
}
