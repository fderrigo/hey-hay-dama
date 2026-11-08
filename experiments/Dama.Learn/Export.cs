using System.Text.Json;
using Dama.Engine;

namespace Dama.Learn;

public static class Export
{
    public static void Run(string source, string destination)
    {
        var network = JsonSerializer.Deserialize<ValueNetwork>(File.ReadAllText(source))!;
        var neurons = new List<Neuron>();
        for (int j = 0; j < ValueNetwork.Hidden; j++)
            neurons.Add(new Neuron("learned-hidden-" + j, network.B[j], network.O[j],
                Enumerable.Range(0, ValueNetwork.Inputs).Select(i => new Connection(i, network.W[i * ValueNetwork.Hidden + j])).ToArray()));
        for (int i = 0; i < ValueNetwork.Inputs; i++)
            neurons.Add(new Neuron("learned-linear-" + i, 0, network.Linear[i], [new Connection(i, 1)]));
        neurons.Add(new Neuron("learned-output-bias", 1, network.Bias, []));
        var weights = new NetworkWeights("dama-relu/v1", "Opale - gameplay learning 2026-11-06", 128,
            "Random initialization; weights learned solely from recorded game outcomes. Export omits monotonic tanh for compatibility; move ordering is preserved.", neurons.ToArray());
        var exported = new NeuralModel(weights);
        // Arithmetic fixtures, not games: check equivalent forward passes on random occupancy arrays.
        var random = new Random(44);
        double largest = 0;
        for (int n = 0; n < 1000; n++)
        {
            var p = new Position(Enumerable.Range(0, 32).Select(_ => random.Next(-2, 3)).ToArray());
            foreach (int side in new[] { -1, 1 })
            {
                double expected = network.Value(ValueNetwork.Encode(p, side));
                double actual = Math.Tanh(exported.Evaluate(p, side));
                largest = Math.Max(largest, Math.Abs(expected - actual));
                if (!double.IsFinite(actual) || Math.Abs(expected - actual) > .0001) throw new InvalidDataException("Export parity failed.");
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.WriteAllText(destination, JsonSerializer.Serialize(weights, NeuralModel.JsonOptions));
        Console.WriteLine(JsonSerializer.Serialize(new { destination, parameters = exported.ParameterCount, verifiedArithmeticInputs = 2000, largestError = largest, network.Updates }));
    }
}
