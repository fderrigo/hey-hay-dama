using Dama.Engine;
using System.Text.Json;

namespace Dama.Learn;

// All parameters start random. No material values, handcrafted evaluation, or imported weights.
// Sparse occupancy inputs, a ReLU hidden layer and a linear skip connection, tanh output.
public sealed class ValueNetwork
{
    public const int Inputs = 128, Hidden = 64;
    public float[] W { get; set; } = new float[Inputs * Hidden];
    public float[] B { get; set; } = new float[Hidden];
    public float[] O { get; set; } = new float[Hidden];
    public float[] Linear { get; set; } = new float[Inputs];
    public float Bias { get; set; }
    public long Updates { get; set; }

    public static ValueNetwork RandomStart(Random random)
    {
        var n = new ValueNetwork();
        for (int i = 0; i < n.W.Length; i++) n.W[i] = (float)(random.NextDouble() - .5) * .2f;
        for (int i = 0; i < Hidden; i++) n.O[i] = (float)(random.NextDouble() - .5) * .05f;
        for (int i = 0; i < Inputs; i++) n.Linear[i] = (float)(random.NextDouble() - .5) * .01f;
        return n;
    }

    public static int[] Encode(Position p, int side)
    {
        var active = new List<int>(24);
        for (int s = 0; s < 32; s++)
        {
            int piece = p.Board[s] * side;
            if (piece == 0) continue;
            int plane = piece switch { 1 => 0, 2 => 1, -1 => 2, _ => 3 };
            active.Add(plane * 32 + (side == 1 ? s : 31 - s));
        }
        return active.ToArray();
    }

    private float Forward(int[] x, Span<float> h)
    {
        B.CopyTo(h);
        float v = Bias;
        foreach (int i in x)
        {
            v += Linear[i];
            int offset = i * Hidden;
            for (int j = 0; j < Hidden; j++) h[j] += W[offset + j];
        }
        for (int j = 0; j < Hidden; j++) { h[j] = Math.Max(0, h[j]); v += h[j] * O[j]; }
        return MathF.Tanh(v);
    }

    public float Value(int[] x)
    {
        Span<float> h = stackalloc float[Hidden];
        return Forward(x, h);
    }

    public float Train(int[] x, float target, float rate)
    {
        Span<float> h = stackalloc float[Hidden];
        float v = Forward(x, h);
        float delta = Math.Clamp(target - v, -1, 1) * (1 - v * v) * rate;
        Bias += delta;
        foreach (int i in x) Linear[i] += delta;
        for (int j = 0; j < Hidden; j++)
        {
            if (h[j] <= 0) continue;
            float d = delta * O[j];
            O[j] += delta * h[j];
            B[j] += d;
            foreach (int i in x) W[i * Hidden + j] += d;
        }
        Updates++;
        return (target - v) * (target - v);
    }

    public void Save(string path)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}
