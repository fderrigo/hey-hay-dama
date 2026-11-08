using System.Text.Json;
using Dama.Engine;

namespace Diaspro;

public sealed class Network
{
    private static readonly int[][] Near = BuildGraph(1);
    private static readonly int[][] Jump = BuildGraph(2);
    private readonly NetworkDefinition definition;

    public Network(NetworkDefinition definition)
    {
        if (definition.Format != "diaspro-relational/v1" || definition.Features.Length != definition.Coefficients.Length
            || definition.Features.Length != 17 || definition.GraphSelf.Length != 6 || definition.GraphNear.Length != 6
            || definition.GraphJump.Length != 6 || definition.GraphHead.Length != 6)
            throw new InvalidDataException("Struttura della rete Diaspro non valida.");
        foreach (var matrix in new[] { definition.GraphSelf, definition.GraphNear, definition.GraphJump })
            if (matrix.Any(row => row.Length != 6 || row.Any(v => !float.IsFinite(v)))) throw new InvalidDataException("Matrice relazionale non valida.");
        if (definition.Coefficients.Any(v => !float.IsFinite(v)) || definition.GraphHead.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("Coefficiente non finito.");
        this.definition = definition;
    }

    public string Name => definition.Name;
    public int InputCount => 32 * 6 + definition.Features.Length;
    public int ParameterCount => definition.Coefficients.Length + definition.GraphHead.Length
        + definition.GraphSelf.Sum(r => r.Length) + definition.GraphNear.Sum(r => r.Length) + definition.GraphJump.Sum(r => r.Length);

    public static Network Load(string path) => new(JsonSerializer.Deserialize<NetworkDefinition>(File.ReadAllBytes(path),
        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Pesi Diaspro vuoti."));

    public float Evaluate(Position position)
    {
        if (Rules.LegalMoves(position).Count == 0) return -20000;
        var rotated = position with { Board = position.Board.Reverse().Select(piece => -piece).ToArray(), Turn = -position.Turn };
        return (EvaluateCore(position) - EvaluateCore(rotated)) / 2f;
    }

    private float EvaluateCore(Position position)
    {
        var features = Features(position);
        var value = 0f;
        for (var i = 0; i < features.Length; i++) value += features[i] * definition.Coefficients[i];
        var graph = Propagate(position);
        for (var i = 0; i < graph.Length; i++) value += graph[i] * definition.GraphHead[i];
        return value;
    }

    public float[] Features(Position position)
    {
        var board = position.Board;
        var turn = position.Turn;
        var legal = Rules.LegalMoves(position);
        if (legal.Count == 0) return [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -20000];
        var opposite = position with { Turn = -turn };
        var otherLegal = Rules.LegalMoves(opposite);
        var result = new float[17];
        result[0] = 1;
        var ownDistance = 7;
        var otherDistance = 7;
        for (var square = 0; square < 32; square++)
        {
            var piece = board[square] * turn;
            if (piece == 0) continue;
            var sign = Math.Sign(piece);
            var rank = Math.Abs(piece);
            var (row, column) = Rules.Coordinates(square);
            var advance = turn == 1 ? 7 - row : row;
            var centrality = (3.5f - Math.Abs(column - 3.5f)) / 3.5f;
            result[1] += sign * (rank == 2 ? 3 : 1);
            if (rank == 1)
            {
                result[2] += sign * advance / 6f;
                if (advance == 0) result[4] += sign;
                if (column is 0 or 7) result[14] += sign;
                if (sign > 0) ownDistance = Math.Min(ownDistance, 7 - advance);
                else otherDistance = Math.Min(otherDistance, 7 - advance);
            }
            else result[6] += sign * centrality;
            result[5] += sign * centrality;
        }
        result[3] = otherDistance - ownDistance;
        result[7] = (QuietMobility(board, turn) - QuietMobility(board, -turn)) / 4f;
        result[8] = (SafeMobility(board, turn) - SafeMobility(board, -turn)) / 4f;
        result[9] = CaptureValue(legal);
        result[10] = -CaptureValue(otherLegal);
        result[11] = CapturePromotion(legal) - CapturePromotion(otherLegal);
        result[12] = Exposed(board, -turn) - Exposed(board, turn);
        result[13] = Supported(board, turn) - Supported(board, -turn);
        for (var square = 0; square < 32; square++)
        {
            var piece = board[square] * turn;
            if (piece == 0) continue;
            var neighbors = Near[square].Count(n => Math.Sign(board[n]) == Math.Sign(board[square]));
            if (neighbors >= 3) result[15] += Math.Sign(piece);
        }
        result[16] = -Math.Sign(result[1]) * position.QuietKingPlyCount / 80f;
        return result;
    }

    public static float Material(int[] board, int turn) => board.Sum(p => p * turn * (Math.Abs(p) == 2 ? 3 : 1));

    private float[] Propagate(Position position)
    {
        var current = new float[32, 6];
        for (var square = 0; square < 32; square++)
        {
            var piece = boardPiece(position.Board[square], position.Turn);
            current[square, 0] = piece == 1 ? 1 : 0;
            current[square, 1] = piece == 2 ? 1 : 0;
            current[square, 2] = piece == -1 ? 1 : 0;
            current[square, 3] = piece == -2 ? 1 : 0;
            current[square, 4] = piece == 0 ? 1 : 0;
            var (_, column) = Rules.Coordinates(square);
            current[square, 5] = column is 0 or 7 ? 1 : 0;
        }
        for (var step = 0; step < 2; step++)
        {
            var next = new float[32, 6];
            for (var square = 0; square < 32; square++)
            for (var channel = 0; channel < 6; channel++)
            {
                var sum = 0f;
                for (var input = 0; input < 6; input++)
                {
                    sum += current[square, input] * definition.GraphSelf[channel][input];
                    foreach (var near in Near[square]) sum += current[near, input] * definition.GraphNear[channel][input] / 4f;
                    foreach (var jump in Jump[square]) sum += current[jump, input] * definition.GraphJump[channel][input] / 4f;
                }
                next[square, channel] = Math.Max(0, sum);
            }
            current = next;
        }
        var pooled = new float[6];
        for (var square = 0; square < 32; square++)
        for (var channel = 0; channel < 6; channel++) pooled[channel] += current[square, channel];
        return pooled;
    }

    private static int boardPiece(int piece, int turn) => piece * turn;

    private static float CaptureValue(IEnumerable<Move> moves)
    {
        var value = 0f;
        foreach (var move in moves)
            if (move.Captures.Length > 0)
                value = Math.Max(value, move.Captures.Sum(_ => 0) + move.VictimRanks.Sum(rank => rank == 2 ? 336 : 112));
        return value;
    }

    private static float CapturePromotion(IEnumerable<Move> moves) => moves.Any(m => m.Captures.Length > 0 && m.Promotes) ? 224 : 0;

    private static int QuietMobility(int[] board, int side)
    {
        var count = 0;
        for (var from = 0; from < 32; from++)
        {
            var piece = board[from];
            if (Math.Sign(piece) != side) continue;
            var (row, column) = Rules.Coordinates(from);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
                if (Rules.Square(row + dr, column + dc) is var to && to >= 0 && board[to] == 0) count++;
        }
        return count;
    }

    private static int SafeMobility(int[] board, int side)
    {
        var count = 0;
        for (var from = 0; from < 32; from++)
        {
            var piece = board[from];
            if (Math.Sign(piece) != side) continue;
            var (row, column) = Rules.Coordinates(from);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                var to = Rules.Square(row + dr, column + dc);
                if (to >= 0 && board[to] == 0 && !DestinationAttacked(board, to, side, from)) count++;
            }
        }
        return count;
    }

    private static bool DestinationAttacked(int[] board, int destination, int side, int origin)
    {
        var (row, column) = Rules.Coordinates(destination);
        for (var attacker = 0; attacker < 32; attacker++)
        {
            var piece = board[attacker];
            if (Math.Sign(piece) != -side) continue;
            var (ar, ac) = Rules.Coordinates(attacker);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                if (Rules.Square(ar + dr, ac + dc) != destination) continue;
                var landing = Rules.Square(ar + 2 * dr, ac + 2 * dc);
                if (landing >= 0 && (board[landing] == 0 || landing == origin)) return true;
            }
        }
        return false;
    }

    private static float Exposed(int[] board, int victimSide)
    {
        var exposed = new HashSet<int>();
        for (var attacker = 0; attacker < 32; attacker++)
        {
            var piece = board[attacker];
            if (Math.Sign(piece) != -victimSide) continue;
            var (row, column) = Rules.Coordinates(attacker);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                var over = Rules.Square(row + dr, column + dc);
                var landing = Rules.Square(row + 2 * dr, column + 2 * dc);
                if (over < 0 || landing < 0 || board[landing] != 0 || Math.Sign(board[over]) != victimSide) continue;
                if (Math.Abs(piece) == 1 && Math.Abs(board[over]) == 2) continue;
                exposed.Add(over);
            }
        }
        return exposed.Sum(square => Math.Abs(board[square]) == 2 ? 3f : 1f);
    }

    private static float Supported(int[] board, int side)
    {
        var value = 0f;
        for (var square = 0; square < 32; square++)
            if (Math.Sign(board[square]) == side && Near[square].Any(n => Math.Sign(board[n]) == side))
                value += Math.Abs(board[square]) == 2 ? 3f : 1f;
        return value;
    }

    private static int[][] BuildGraph(int distance)
    {
        var graph = new int[32][];
        for (var square = 0; square < 32; square++)
        {
            var (row, column) = Rules.Coordinates(square);
            var edges = new List<int>();
            foreach (var dr in new[] { -distance, distance })
            foreach (var dc in new[] { -distance, distance })
                if (Rules.Square(row + dr, column + dc) is var other && other >= 0) edges.Add(other);
            graph[square] = edges.ToArray();
        }
        return graph;
    }
}
