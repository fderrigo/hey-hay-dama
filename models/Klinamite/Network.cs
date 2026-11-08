using System.Text.Json;

namespace Klinamite;

public sealed record NeuronDefinition(int Layer, float Bias, int[] From, float[] Weights, string? Label = null);
public sealed record NetworkDefinition(string Format, string Name, string Construction, int Inputs, NeuronDefinition[] Neurons);

public sealed class Network
{
    public const string FormatName = "klinamite-net/v2";
    public const int InputCount = 161;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly NetworkDefinition definition;

    public Network(NetworkDefinition definition)
    {
        if (definition.Format != FormatName || definition.Inputs != InputCount || definition.Neurons.Length < 2)
            throw new InvalidDataException("Formato della rete Klinamite non valido.");
        for (var i = 0; i < definition.Neurons.Length; i++)
        {
            var n = definition.Neurons[i];
            if (n.From.Length != n.Weights.Length || !float.IsFinite(n.Bias))
                throw new InvalidDataException($"Neurone {i} non valido.");
            for (var j = 0; j < n.From.Length; j++)
                if (n.From[j] < 0 || n.From[j] >= InputCount + i || !float.IsFinite(n.Weights[j]))
                    throw new InvalidDataException($"Connessione non valida verso {InputCount + i}.");
        }
        this.definition = definition;
    }

    public int NeuronCount => definition.Neurons.Length;
    public int ConnectionCount => definition.Neurons.Sum(n => n.From.Length);

    public static Network Load(string path) => new(JsonSerializer.Deserialize<NetworkDefinition>(File.ReadAllBytes(path), Json)
        ?? throw new InvalidDataException("File dei pesi Klinamite vuoto."));

    public static byte[] Serialize(NetworkDefinition definition) => JsonSerializer.SerializeToUtf8Bytes(definition, Json);

    public float Evaluate(ReadOnlySpan<int> board, int turn, bool immediateKingReturn = false)
    {
        var values = new float[InputCount + definition.Neurons.Length];
        for (var square = 0; square < 32; square++)
        {
            var piece = board[square] * turn;
            var plane = piece switch { 0 => 4, 1 => 0, 2 => 1, -1 => 2, _ => 3 };
            values[plane * 32 + (turn == 1 ? square : 31 - square)] = 1;
        }
        values[160] = immediateKingReturn ? 1 : 0;
        for (var i = 0; i < definition.Neurons.Length; i++)
        {
            var n = definition.Neurons[i];
            var sum = n.Bias;
            for (var j = 0; j < n.From.Length; j++) sum += values[n.From[j]] * n.Weights[j];
            values[InputCount + i] = i == definition.Neurons.Length - 1 ? sum : Math.Max(0, sum);
        }
        return values[^1];
    }
}

public sealed record Candidate(int From, int To, int[] Captures, bool Promotes);

public static class Policy
{
    public static int[] After(int[] board, Candidate move)
    {
        var next = (int[])board.Clone();
        var piece = next[move.From];
        next[move.From] = 0;
        foreach (var square in move.Captures) next[square] = 0;
        next[move.To] = move.Promotes ? 2 * Math.Sign(piece) : piece;
        return next;
    }

    public static float[] Values(Network network, int[] board, int turn, IReadOnlyList<Candidate> moves,
        IReadOnlyList<RecentMoveDto>? recentMoves = null, string? player = null)
    {
        var values = new float[moves.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var isReturn = IsImmediateKingReturn(board, moves[i], recentMoves, player);
            values[i] = -network.Evaluate(After(board, moves[i]), -turn, isReturn);
        }
        return values;
    }

    private static bool IsImmediateKingReturn(int[] board, Candidate move, IReadOnlyList<RecentMoveDto>? recentMoves, string? player)
    {
        if (move.Captures.Length != 0 || Math.Abs(board[move.From]) != 2 || recentMoves is null || player is null) return false;
        var previous = recentMoves.LastOrDefault(m => m is not null && m.Player == player);
        if (previous is null || !TryEndpoints(previous.MoveId, out var from, out var to)) return false;
        return move.From == to && move.To == from;
    }

    private static bool TryEndpoints(string? moveId, out int from, out int to)
    {
        from = to = -1;
        if (moveId is null) return false;
        var parts = moveId.Split(['-', 'x']);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var first) || !int.TryParse(parts[1], out var last)) return false;
        if (first is < 1 or > 32 || last is < 1 or > 32) return false;
        from = first - 1;
        to = last - 1;
        return true;
    }

    public static int Choose(float[] values)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++) if (values[i] > values[best]) best = i;
        return best;
    }
}
