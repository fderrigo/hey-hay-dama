using Agata;

namespace Agata.Sfida;

/// <summary>
/// Online updates on an agata-net/v1 graph. Only the connections into the output neuron and the neurons
/// labelled "sfida" change; the original hidden weights stay as they are. Steps are normalised (NLMS) in
/// value units, because hidden activations vary widely in scale.
/// </summary>
public sealed class Trainer
{
    public const string Added = "sfida";
    public const double Scale = 300;
    private readonly NetworkDefinition definition;
    private readonly float[] bias, weight;
    private readonly int[] start, target, slot, owner, outEdge, newStart;
    private readonly int firstNew, last, added;

    public Trainer(NetworkDefinition source, int addNeurons, int seed)
    {
        definition = Grow(source, addNeurons, seed);
        var neurons = definition.Neurons;
        added = neurons.Count(n => n.Label == Added);
        var total = Network.InputCount + neurons.Length;
        last = total - 1;
        firstNew = last - added;
        bias = new float[total];
        var outgoing = new List<(int Target, int Slot)>[total];
        for (var i = 0; i < total; i++) outgoing[i] = [];
        for (var i = 0; i < neurons.Length; i++)
        {
            bias[Network.InputCount + i] = neurons[i].Bias;
            for (var c = 0; c < neurons[i].From.Length; c++) outgoing[neurons[i].From[c]].Add((Network.InputCount + i, c));
        }
        start = new int[total + 1];
        for (var i = 0; i < total; i++) start[i + 1] = start[i] + outgoing[i].Count;
        target = new int[start[total]]; slot = new int[start[total]]; owner = new int[start[total]]; weight = new float[start[total]];
        outEdge = new int[total]; Array.Fill(outEdge, -1);
        newStart = new int[Network.InputCount];
        for (var i = 0; i < total; i++)
            for (var c = 0; c < outgoing[i].Count; c++)
            {
                var e = start[i] + c; var (t, s) = outgoing[i][c];
                target[e] = t; slot[e] = s; owner[e] = i;
                weight[e] = neurons[t - Network.InputCount].Weights[s];
                if (t == last) outEdge[i] = e;
            }
        for (var i = 0; i < Network.InputCount; i++) newStart[i] = start[i + 1] - added;
    }

    private static NetworkDefinition Grow(NetworkDefinition source, int count, int seed)
    {
        if (count == 0 || source.Neurons.Any(n => n.Label == Added)) return source;
        var r = new Random(seed);
        var output = source.Neurons[^1];
        var firstId = Network.InputCount + source.Neurons.Length - 1;
        var extra = Enumerable.Range(0, count).Select(_ => new NeuronDefinition(1, .3f, Enumerable.Range(0, Network.InputCount).ToArray(),
            Enumerable.Range(0, Network.InputCount).Select(_ => (float)((r.NextDouble() - .5) * .6)).ToArray(), Added));
        var grown = output with
        {
            From = [.. output.From, .. Enumerable.Range(firstId, count)],
            Weights = [.. output.Weights, .. Enumerable.Repeat(0f, count)],
        };
        return source with { Neurons = [.. source.Neurons[..^1], .. extra, grown] };
    }

    public static int Encode(ReadOnlySpan<int> board, int turn, int[] active)
    {
        for (var s = 0; s < 32; s++)
        {
            var p = board[s] * turn;
            var plane = p switch { 0 => Network.Empty, 1 => Network.OwnMan, 2 => Network.OwnKing, -1 => Network.EnemyMan, _ => Network.EnemyKing };
            active[s] = plane * 32 + (turn == 1 ? s : 31 - s);
        }
        return 32;
    }

    public float[] Buffer() => new float[bias.Length];

    /// <summary>One normalised step towards a target in [-1, 1]. Shared across threads on purpose.</summary>
    public void Update(ReadOnlySpan<byte> active, float goal, float rate, float[] values)
    {
        Array.Copy(bias, values, values.Length);
        foreach (var input in active) values[input] = 1;
        for (var i = 0; i < last; i++)
        {
            var a = values[i];
            if (a <= 0) continue;
            for (var e = start[i]; e < start[i + 1]; e++) values[target[e]] += weight[e] * a;
        }
        var wanted = Scale * Math.Atanh(Math.Clamp(goal, -.98, .98));
        var error = Math.Clamp(wanted - values[last], -300, 300);
        double norm = 1;
        for (var i = 0; i < last; i++)
            if (outEdge[i] >= 0 && values[i] > 0) norm += values[i] * values[i];
        for (var t = firstNew; t < last; t++)
            if (values[t] > 0) { var o = weight[outEdge[t]]; norm += 33 * o * o; }
        var k = (float)(rate * error / norm);
        bias[last] += k;
        for (var i = 0; i < last; i++)
            if (outEdge[i] >= 0 && values[i] > 0) weight[outEdge[i]] += k * values[i];
        for (var t = firstNew; t < last; t++)
        {
            if (values[t] <= 0) continue;
            var g = k * weight[outEdge[t]];
            bias[t] += g;
            foreach (var input in active) weight[newStart[input] + t - firstNew] += g;
        }
    }

    public NetworkDefinition Export()
    {
        var neurons = definition.Neurons.Select((n, i) => n with { Bias = bias[Network.InputCount + i], Weights = (float[])n.Weights.Clone() }).ToArray();
        for (var e = 0; e < weight.Length; e++) neurons[target[e] - Network.InputCount].Weights[slot[e]] = weight[e];
        return definition with { Neurons = neurons };
    }
}

/// <summary>Remembered states and the targets their games produced.</summary>
public sealed class Memory(int capacity)
{
    private readonly byte[] states = new byte[capacity * 32];
    private readonly float[] goals = new float[capacity];
    private long written;

    public void Add(int[] active, float goal)
    {
        var i = (int)(written++ % capacity);
        for (var k = 0; k < 32; k++) states[i * 32 + k] = (byte)active[k];
        goals[i] = goal;
    }

    public void Replay(Trainer trainer, int samples, float rate, float[] values, Random r)
    {
        var size = (int)Math.Min(written, capacity);
        for (var e = 0; e < samples && size > 0; e++)
        {
            var i = r.Next(size);
            trainer.Update(states.AsSpan(i * 32, 32), goals[i], rate, values);
        }
    }
}
