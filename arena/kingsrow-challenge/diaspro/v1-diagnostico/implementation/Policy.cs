using Dama.Engine;

namespace Diaspro;

public static class Policy
{
    public static float[] Values(Network network, Position position, IReadOnlyList<Move> legal,
        PositionMemory? memory = null, string? gameId = null)
    {
        var values = new float[legal.Count];
        for (var i = 0; i < legal.Count; i++)
        {
            var next = Rules.Apply(position, legal[i]);
            var repetitions = memory?.CountNext(gameId, next.Key) ?? 0;
            var material = Network.Material(next.Board, position.Turn);
            var penalty = repetitions * (material > 0 ? 24f : material < 0 ? 3f : 12f);
            values[i] = -network.Evaluate(next) - Math.Min(96, penalty);
        }
        return values;
    }

    public static int Choose(IReadOnlyList<float> values)
    {
        var best = 0;
        for (var i = 1; i < values.Count; i++) if (values[i] > values[best]) best = i;
        return best;
    }
}
