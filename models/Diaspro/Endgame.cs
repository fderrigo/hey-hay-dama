using System.Numerics;
using Dama.Engine;

namespace Diaspro;

public static class Endgame
{
    public static float Evaluate(BitPosition position, IReadOnlyList<Move> legal)
    {
        var whiteMen = BitOperations.PopCount(position.WhiteMen);
        var blackMen = BitOperations.PopCount(position.BlackMen);
        var whiteKings = BitOperations.PopCount(position.WhiteKings);
        var blackKings = BitOperations.PopCount(position.BlackKings);
        var material = (whiteMen + 3 * whiteKings - blackMen - 3 * blackKings) * position.Turn;
        var value = material * 112f;
        var ownDistance = 7;
        var otherDistance = 7;
        for (var square = 0; square < 32; square++)
        {
            var piece = position.PieceAt(square);
            if (piece == 0) continue;
            var sign = Math.Sign(piece) * position.Turn;
            var (row, column) = Rules.Coordinates(square);
            if (Math.Abs(piece) == 1)
            {
                var advance = piece == 1 ? 7 - row : row;
                value += sign * advance * 5f;
                if (sign > 0) ownDistance = Math.Min(ownDistance, 7 - advance);
                else otherDistance = Math.Min(otherDistance, 7 - advance);
            }
            else
            {
                value += sign * (3.5f - Math.Abs(column - 3.5f)) / 3.5f * 4f;
                var nearest = 16;
                for (var enemy = 0; enemy < 32; enemy++)
                {
                    if (Math.Sign(position.PieceAt(enemy)) != -Math.Sign(piece)) continue;
                    var (enemyRow, enemyColumn) = Rules.Coordinates(enemy);
                    nearest = Math.Min(nearest, Math.Max(Math.Abs(row - enemyRow), Math.Abs(column - enemyColumn)));
                }
                if (nearest < 16) value += sign * (7 - nearest) * 2f;
            }
        }
        value += (otherDistance - ownDistance) * 8f;
        var otherLegal = BitRules.LegalMoves(position with { Turn = -position.Turn });
        var ownMobility = legal.Count(move => move.Captures.Length == 0);
        var otherMobility = otherLegal.Count(move => move.Captures.Length == 0);
        value += (ownMobility - otherMobility) * 3f;
        value -= Math.Sign(material) * position.QuietKingPlyCount / 80f * 0.35f;
        return value;
    }
}
