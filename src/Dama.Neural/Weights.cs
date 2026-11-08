using Dama.Engine;

namespace Dama.Neural;

public static class KimberliteWeightBuilder
{
    public static NetworkWeights Build()
    {
        var neurons = new List<Neuron>();
        void Add(string feature, double output, double bias, params Weight[] weights) =>
            neurons.Add(new Neuron(feature, bias, output, weights));

        for (var side = 0; side < 2; side++)
        {
            var sign = side == 0 ? 1 : -1;
            var ownMan = side * 64;
            var ownKing = ownMan + 32;
            var enemyMan = (1 - side) * 64;
            var enemyKing = enemyMan + 32;
            var forward = side == 0 ? -1 : 1;
            for (var square = 0; square < 32; square++)
            {
                var (row, column) = Rules.Coordinates(square);
                var advancement = side == 0 ? 7 - row : row;
                var centre = Math.Min(row, 7 - row) + Math.Min(column, 7 - column);
                Add("material-man", sign * 100, 0, new Weight(ownMan + square, 1));
                Add("material-king", sign * 300, 0, new Weight(ownKing + square, 1));
                Add("promotion-progress", sign * (advancement * 2 + advancement * advancement * .55), 0, new Weight(ownMan + square, 1));
                Add("centre-man", sign * centre * 1.5, 0, new Weight(ownMan + square, 1));
                Add("centre-king", sign * centre * 3, 0, new Weight(ownKing + square, 1));
                if (advancement == 0) Add("home-guard", sign * 12, 0, new Weight(ownMan + square, 1));
                if (column is 0 or 7) Add("edge-safety", sign * 2, 0, new Weight(ownMan + square, 1));

                foreach (var dr in new[] { -1, 1 })
                foreach (var dc in new[] { -1, 1 })
                {
                    var adjacent = Rules.Square(row + dr, column + dc);
                    if (adjacent < 0) continue;
                    if (dr == forward)
                        Add("man-mobility", sign * 3, -1, [new Weight(ownMan + square, 1), ..Empty(adjacent)]);
                    Add("king-mobility", sign * 4, -1, [new Weight(ownKing + square, 1), ..Empty(adjacent)]);
                    if (dr == -forward)
                    {
                        Add("man-formation", sign * 7, -1, new Weight(ownMan + square, 1), new Weight(ownMan + adjacent, 1));
                        Add("king-formation", sign * 8, -1, new Weight(ownKing + square, 1), new Weight(ownMan + adjacent, 1));
                    }

                    var landing = Rules.Square(row + dr * 2, column + dc * 2);
                    if (landing >= 0)
                    {
                        if (dr == forward)
                            Add("man-jump", sign * 45, -1, [new Weight(ownMan + square, 1), new Weight(enemyMan + adjacent, 1), ..Empty(landing)]);
                        Add("king-jump-man", sign * 45, -1, [new Weight(ownKing + square, 1), new Weight(enemyMan + adjacent, 1), ..Empty(landing)]);
                        Add("king-jump-king", sign * 80, -1, [new Weight(ownKing + square, 1), new Weight(enemyKing + adjacent, 1), ..Empty(landing)]);
                    }

                    var counterLanding = Rules.Square(row - dr, column - dc);
                    if (counterLanding < 0) continue;
                    if (dr == -forward)
                    {
                        Add("man-en-prise", sign * -40, -1, [new Weight(ownMan + square, 1), new Weight(enemyMan + adjacent, 1), ..Empty(counterLanding)]);
                        Add("king-en-prise-by-man", sign * -75, -1, [new Weight(ownKing + square, 1), new Weight(enemyMan + adjacent, 1), ..Empty(counterLanding)]);
                    }
                    Add("man-en-prise-by-king", sign * -45, -1, [new Weight(ownMan + square, 1), new Weight(enemyKing + adjacent, 1), ..Empty(counterLanding)]);
                    Add("king-en-prise-by-king", sign * -85, -1, [new Weight(ownKing + square, 1), new Weight(enemyKing + adjacent, 1), ..Empty(counterLanding)]);
                }
            }
        }
        return new NetworkWeights("kimberlite-relu/v1", "Kimberlite v1", 128,
            "Pesi scritti direttamente per materiale, promozione, centralita, mobilita, formazioni, salti ed esposizione. Nessun training, fine-tuning o ottimizzazione automatica.", neurons.ToArray());
    }

    private static Weight[] Empty(int square) => Enumerable.Range(0, 4)
        .Select(plane => new Weight(plane * 32 + square, -1)).ToArray();
}
