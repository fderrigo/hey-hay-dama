using System.Text.Json;

namespace Agata;

public sealed record NeuronDefinition(int Layer, float Bias, int[] From, float[] Weights, string? Label = null);
public sealed record NetworkDefinition(string Format, string Name, string Construction, int Inputs, NeuronDefinition[] Neurons);

/// <summary>
/// Sparse feed-forward ReLU network. The code knows no draughts: all game knowledge is in the weights
/// file. Layers may skip forward. The last neuron is linear and gives the value of the position for
/// the side to move, in hundredths of a man. Instances are immutable and shared between requests.
/// </summary>
public sealed class Network
{
    public const string FormatName = "agata-net/v1";
    public const int InputCount = 5 * 32;
    public const int OwnMan = 0, OwnKing = 1, EnemyMan = 2, EnemyKing = 3, Empty = 4;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly float[] bias;
    private readonly int[] start, target;
    private readonly float[] weight;
    public int NeuronCount => bias.Length;
    public int ConnectionCount => target.Length;

    public Network(NetworkDefinition definition)
    {
        if (definition.Format != FormatName || definition.Inputs != InputCount || definition.Neurons.Length == 0)
            throw new InvalidDataException("Formato dei pesi non supportato.");
        var total = InputCount + definition.Neurons.Length;
        var outgoing = new List<(int Target, float Weight)>[total];
        for (var i = 0; i < total; i++) outgoing[i] = [];
        bias = new float[total];
        for (var i = 0; i < definition.Neurons.Length; i++)
        {
            var neuron = definition.Neurons[i];
            var id = InputCount + i;
            if (neuron.From.Length != neuron.Weights.Length || !float.IsFinite(neuron.Bias)) throw new InvalidDataException($"Neurone {id} non valido.");
            bias[id] = neuron.Bias;
            for (var c = 0; c < neuron.From.Length; c++)
            {
                if (neuron.From[c] < 0 || neuron.From[c] >= id || !float.IsFinite(neuron.Weights[c])) throw new InvalidDataException($"Connessione non valida verso {id}.");
                outgoing[neuron.From[c]].Add((id, neuron.Weights[c]));
            }
        }
        start = new int[total + 1];
        for (var i = 0; i < total; i++) start[i + 1] = start[i] + outgoing[i].Count;
        target = new int[start[total]];
        weight = new float[start[total]];
        for (var i = 0; i < total; i++)
            for (var c = 0; c < outgoing[i].Count; c++)
                (target[start[i] + c], weight[start[i] + c]) = outgoing[i][c];
    }

    public static Network Load(string path) =>
        new(JsonSerializer.Deserialize<NetworkDefinition>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("File dei pesi vuoto."));

    /// <summary>Forward pass. The side to move is always seen moving up, so colours need no special case.</summary>
    public float Evaluate(ReadOnlySpan<int> board, int turn)
    {
        var values = new float[bias.Length];
        Array.Copy(bias, values, values.Length);
        for (var square = 0; square < 32; square++)
        {
            var piece = board[square] * turn;
            var plane = piece switch { 0 => Empty, 1 => OwnMan, 2 => OwnKing, -1 => EnemyMan, _ => EnemyKing };
            values[plane * 32 + (turn == 1 ? square : 31 - square)] = 1;
        }
        var last = values.Length - 1;
        for (var i = 0; i < last; i++)
        {
            var activation = values[i];
            if (activation <= 0) continue; // ReLU; inputs are 0 or 1, so the same test serves them.
            for (var e = start[i]; e < start[i + 1]; e++) values[target[e]] += weight[e] * activation;
        }
        return values[last];
    }
}

public sealed record Candidate(int From, int To, int[] Captures, bool Promotes);

public static class Policy
{
    /// <summary>The board after a candidate. Squares here are 0-based. This only copies out what the game sent.</summary>
    public static int[] After(int[] board, Candidate move)
    {
        var next = (int[])board.Clone();
        var piece = next[move.From];
        next[move.From] = 0;
        foreach (var square in move.Captures) next[square] = 0;
        next[move.To] = move.Promotes ? 2 * Math.Sign(piece) : piece;
        return next;
    }

    /// <summary>Network value of each candidate, for the side playing it.</summary>
    public static float[] Values(Network network, int[] board, int turn, IReadOnlyList<Candidate> moves)
    {
        var values = new float[moves.Count];
        for (var i = 0; i < values.Length; i++) values[i] = -network.Evaluate(After(board, moves[i]), -turn);
        return values;
    }

    /// <summary>Samples from a softmax over the values. Temperature 0 always takes the highest value.</summary>
    public static int Choose(float[] values, double temperature, Random random)
    {
        var best = values.Max();
        if (temperature <= 0) return Array.IndexOf(values, best);
        var weights = values.Select(v => Math.Exp((v - best) / temperature)).ToArray();
        var pick = random.NextDouble() * weights.Sum();
        for (var i = 0; i < weights.Length; i++)
        {
            pick -= weights[i];
            if (pick <= 0) return i;
        }
        return Array.IndexOf(values, best);
    }
}
