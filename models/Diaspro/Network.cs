using System.Runtime.Intrinsics;
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
            || definition.Features.Length != 19 || definition.GraphSelf.Length != 6 || definition.GraphNear.Length != 6
            || definition.GraphJump.Length != 6 || definition.GraphHead.Length != 6)
            throw new InvalidDataException("Struttura della rete Diaspro non valida.");
        foreach (var matrix in new[] { definition.GraphSelf, definition.GraphNear, definition.GraphJump })
            if (matrix.Any(row => row.Length != 6 || row.Any(v => !float.IsFinite(v)))) throw new InvalidDataException("Matrice relazionale non valida.");
        if (definition.Coefficients.Any(v => !float.IsFinite(v)) || definition.GraphHead.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("Coefficiente non finito.");
        if (definition.PatternSquare is not null && (definition.PatternSquare.Length != 4
            || definition.PatternSquare.Any(row => row.Length != 32 || row.Any(v => !float.IsFinite(v))))
            || definition.PatternNear is not null && (definition.PatternNear.Length != 128
            || definition.PatternNear.Any(row => row.Length != 16 || row.Any(v => !float.IsFinite(v)))))
            throw new InvalidDataException("Testa di pattern non valida.");
        this.definition = definition;
    }

    public string Name => definition.Name;
    public int InputCount => 32 * 6 + definition.Features.Length;
    public int ParameterCount => definition.Coefficients.Length + definition.GraphHead.Length
        + definition.GraphSelf.Sum(r => r.Length) + definition.GraphNear.Sum(r => r.Length) + definition.GraphJump.Sum(r => r.Length)
        + (definition.PatternSquare?.Sum(r => r.Length) ?? 0) + (definition.PatternNear?.Sum(r => r.Length) ?? 0);

    public static Network Load(string path) => new(JsonSerializer.Deserialize<NetworkDefinition>(File.ReadAllBytes(path),
        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Pesi Diaspro vuoti."));

    public float Evaluate(Position position) => Evaluate(position, FastRules.LegalMoves(position));

    public float Evaluate(Position position, IReadOnlyList<Move> legal) => EvaluateCore(position, legal, false);

    public float EvaluateFast(Position position, IReadOnlyList<Move> legal) => EvaluateCore(position, legal, true);

    public float EvaluateFast(BitPosition position, IReadOnlyList<Move> legal)
    {
        if (legal.Count == 0) return -20000;
        var features = Features(position, legal);
        var value = 0f;
        for (var i = 0; i < features.Length; i++) value += features[i] * definition.Coefficients[i];
        var graph = PropagateVector(position);
        var rotatedGraph = PropagateVector(position.Rotate());
        var channelMap = new[] { 2, 3, 0, 1, 4, 5 };
        for (var channel = 0; channel < graph.Length; channel++)
            value += (graph[channel] + rotatedGraph[channelMap[channel]]) * 0.5f * definition.GraphHead[channel];
        value += PatternValue(position);
        return value;
    }

    public const float PatternScale = 0.5f;

    private float PatternValue(Position position) => PatternValue(BitPosition.FromPosition(position));

    private float PatternValue(BitPosition position) => definition.PatternSquare is null && definition.PatternNear is null
        ? 0 : (float)((PatternRaw(position) + PatternRaw(position.Rotate())) * 0.5 * PatternScale);

    private double PatternRaw(BitPosition position)
    {
        var value = 0d;
        for (var from = 0; from < 32; from++)
        {
            var piece = position.PieceAt(from) * position.Turn;
            if (piece == 0) continue;
            var channel = PatternChannel(piece);
            if (definition.PatternSquare is not null) value += definition.PatternSquare[channel][from];
            if (definition.PatternNear is null) continue;
            var (row, column) = Rules.Coordinates(from);
            for (var direction = 0; direction < 4; direction++)
            {
                var to = Rules.Square(row + (direction < 2 ? -1 : 1), column + (direction % 2 == 0 ? -1 : 1));
                if (to < 0) continue;
                var neighbor = position.PieceAt(to) * position.Turn;
                if (neighbor == 0) continue;
                value += definition.PatternNear[from * 4 + direction][channel * 4 + PatternChannel(neighbor)];
            }
        }
        return value;
    }

    private static int PatternChannel(int piece) => piece == 1 ? 0 : piece == 2 ? 1 : piece == -1 ? 2 : 3;

    private float[] Features(BitPosition position, IReadOnlyList<Move> legal)
    {
        var turn = position.Turn;
        if (legal.Count == 0) return [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -20000, 0, 0];
        var otherLegal = BitRules.LegalMoves(position with { Turn = -turn });
        var result = new float[19];
        result[0] = 1;
        var ownDistance = 7;
        var otherDistance = 7;
        for (var square = 0; square < 32; square++)
        {
            var actualPiece = position.PieceAt(square);
            var piece = actualPiece * turn;
            if (piece == 0) continue;
            var sign = Math.Sign(piece);
            var rank = Math.Abs(piece);
            var (row, column) = Rules.Coordinates(square);
            var advance = Math.Sign(actualPiece) == 1 ? 7 - row : row;
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
        result[7] = (QuietMobility(position, turn) - QuietMobility(position, -turn)) / 4f;
        result[8] = (SafeMobility(position, turn) - SafeMobility(position, -turn)) / 4f;
        result[9] = CaptureValue(legal);
        result[10] = -CaptureValue(otherLegal);
        result[11] = CapturePromotion(legal) - CapturePromotion(otherLegal);
        var ownExposure = TacticalExposure(position, -turn);
        var otherExposure = TacticalExposure(position, turn);
        result[12] = ownExposure.Exposed - otherExposure.Exposed;
        result[13] = Supported(position, turn) - Supported(position, -turn);
        for (var square = 0; square < 32; square++)
        {
            var actualPiece = position.PieceAt(square);
            var piece = actualPiece * turn;
            if (piece == 0) continue;
            var neighbors = Near[square].Count(n => Math.Sign(position.PieceAt(n)) == Math.Sign(actualPiece));
            if (neighbors >= 3) result[15] += Math.Sign(piece);
        }
        result[16] = -Math.Sign(result[1]) * position.QuietKingPlyCount / 80f;
        result[17] = ownExposure.Undefended - otherExposure.Undefended;
        result[18] = ownExposure.Continued - otherExposure.Continued;
        return result;
    }

    private float EvaluateCore(Position position, IReadOnlyList<Move> legal, bool vector)
    {
        if (legal.Count == 0) return -20000;
        var features = Features(position, legal);
        var value = 0f;
        for (var i = 0; i < features.Length; i++) value += features[i] * definition.Coefficients[i];
        var rotated = position with { Board = position.Board.Reverse().Select(piece => -piece).ToArray(), Turn = -position.Turn };
        var graph = vector ? PropagateVector(position) : Propagate(position);
        var rotatedGraph = vector ? PropagateVector(rotated) : Propagate(rotated);
        var channelMap = new[] { 2, 3, 0, 1, 4, 5 };
        for (var channel = 0; channel < graph.Length; channel++)
            value += (graph[channel] + rotatedGraph[channelMap[channel]]) * 0.5f * definition.GraphHead[channel];
        value += PatternValue(position);
        return value;
    }

    public float[] Features(Position position) => Features(position, FastRules.LegalMoves(position));

    private float[] Features(Position position, IReadOnlyList<Move> legal)
    {
        var board = position.Board;
        var turn = position.Turn;
        if (legal.Count == 0) return [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -20000, 0, 0];
        var opposite = position with { Turn = -turn };
        var otherLegal = FastRules.LegalMoves(opposite);
        var result = new float[19];
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
            var advance = Math.Sign(board[square]) == 1 ? 7 - row : row;
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
        var ownExposure = TacticalExposure(board, -turn);
        var otherExposure = TacticalExposure(board, turn);
        result[12] = ownExposure.Exposed - otherExposure.Exposed;
        result[13] = Supported(board, turn) - Supported(board, -turn);
        for (var square = 0; square < 32; square++)
        {
            var piece = board[square] * turn;
            if (piece == 0) continue;
            var neighbors = Near[square].Count(n => Math.Sign(board[n]) == Math.Sign(board[square]));
            if (neighbors >= 3) result[15] += Math.Sign(piece);
        }
        result[16] = -Math.Sign(result[1]) * position.QuietKingPlyCount / 80f;
        result[17] = ownExposure.Undefended - otherExposure.Undefended;
        result[18] = ownExposure.Continued - otherExposure.Continued;
        return result;
    }

    public static float Material(int[] board, int turn) => board.Sum(p => p * turn * (Math.Abs(p) == 2 ? 3 : 1));

    private GraphVector[][]? vectorWeights;

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

    private float[] PropagateVector(Position position) => PropagateVector(BitPosition.FromPosition(position));

    private float[] PropagateVector(BitPosition position)
    {
        var weights = vectorWeights ??= CompileVectorWeights();
        var current = new GraphVector[32];
        for (var square = 0; square < 32; square++)
        {
            var piece = boardPiece(position.PieceAt(square), position.Turn);
            var (_, column) = Rules.Coordinates(square);
            current[square] = new GraphVector(
                Vector128.Create(piece == 1 ? 1f : 0f, piece == 2 ? 1f : 0f, piece == -1 ? 1f : 0f, piece == -2 ? 1f : 0f),
                Vector128.Create(piece == 0 ? 1f : 0f, column is 0 or 7 ? 1f : 0f, 0f, 0f));
        }
        for (var step = 0; step < 2; step++)
        {
            var next = new GraphVector[32];
            for (var square = 0; square < 32; square++)
            {
                var sum = GraphVector.Zero;
                for (var input = 0; input < 6; input++)
                {
                    sum += current[square][input] * weights[0][input];
                    foreach (var near in Near[square]) sum += current[near][input] * weights[1][input] / 4f;
                    foreach (var jump in Jump[square]) sum += current[jump][input] * weights[2][input] / 4f;
                }
                next[square] = GraphVector.MaxZero(sum);
            }
            current = next;
        }
        var total = GraphVector.Zero;
        foreach (var square in current) total += square;
        var pooled = new float[6];
        for (var channel = 0; channel < 6; channel++) pooled[channel] = total[channel];
        return pooled;
    }

    private GraphVector[][] CompileVectorWeights()
    {
        var matrices = new[] { definition.GraphSelf, definition.GraphNear, definition.GraphJump };
        var result = new GraphVector[3][];
        for (var relation = 0; relation < 3; relation++)
        {
            result[relation] = new GraphVector[6];
            for (var input = 0; input < 6; input++)
                result[relation][input] = new GraphVector(
                    Vector128.Create(matrices[relation][0][input], matrices[relation][1][input], matrices[relation][2][input], matrices[relation][3][input]),
                    Vector128.Create(matrices[relation][4][input], matrices[relation][5][input], 0f, 0f));
        }
        return result;
    }

    private readonly record struct GraphVector(Vector128<float> Low, Vector128<float> High)
    {
        public static GraphVector Zero => new(Vector128<float>.Zero, Vector128<float>.Zero);
        public float this[int channel] => channel < 4 ? Low[channel] : High[channel - 4];
        public static GraphVector operator +(GraphVector left, GraphVector right) => new(left.Low + right.Low, left.High + right.High);
        public static GraphVector operator *(float scalar, GraphVector vector) => new(scalar * vector.Low, scalar * vector.High);
        public static GraphVector operator /(GraphVector value, float divisor) => new(value.Low / divisor, value.High / divisor);
        public static GraphVector MaxZero(GraphVector value) => new(Vector128.Max(value.Low, Vector128<float>.Zero), Vector128.Max(value.High, Vector128<float>.Zero));
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

    private static int QuietMobility(BitPosition position, int side)
    {
        var count = 0;
        for (var from = 0; from < 32; from++)
        {
            var piece = position.PieceAt(from);
            if (Math.Sign(piece) != side) continue;
            var (row, column) = Rules.Coordinates(from);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
                if (Rules.Square(row + dr, column + dc) is var to && to >= 0 && position.PieceAt(to) == 0) count++;
        }
        return count;
    }

    private static int SafeMobility(BitPosition position, int side)
    {
        var count = 0;
        for (var from = 0; from < 32; from++)
        {
            var piece = position.PieceAt(from);
            if (Math.Sign(piece) != side) continue;
            var (row, column) = Rules.Coordinates(from);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                var to = Rules.Square(row + dr, column + dc);
                if (to >= 0 && position.PieceAt(to) == 0 && !DestinationAttacked(position, to, side, from)) count++;
            }
        }
        return count;
    }

    private static bool DestinationAttacked(BitPosition position, int destination, int side, int origin)
    {
        var (row, column) = Rules.Coordinates(destination);
        foreach (var dr in new[] { -1, 1 })
        foreach (var dc in new[] { -1, 1 })
        {
            var attacker = Rules.Square(row - dr, column - dc);
            var landing = Rules.Square(row + dr, column + dc);
            if (attacker < 0 || landing < 0 || Math.Sign(position.PieceAt(attacker)) != -side) continue;
            if (!Rules.Directions(position.PieceAt(attacker)).Contains(dr)) continue;
            if (position.PieceAt(landing) == 0 || landing == origin) return true;
        }
        return false;
    }

    private static (float Exposed, float Undefended, float Continued) TacticalExposure(BitPosition position, int victimSide)
    {
        var exposed = new HashSet<int>();
        var undefended = new HashSet<int>();
        var continued = 0f;
        for (var attacker = 0; attacker < 32; attacker++)
        {
            var piece = position.PieceAt(attacker);
            if (Math.Sign(piece) != -victimSide) continue;
            var (row, column) = Rules.Coordinates(attacker);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                var over = Rules.Square(row + dr, column + dc);
                var landing = Rules.Square(row + 2 * dr, column + 2 * dc);
                if (over < 0 || landing < 0 || position.PieceAt(landing) != 0 || Math.Sign(position.PieceAt(over)) != victimSide) continue;
                if (Math.Abs(piece) == 1 && Math.Abs(position.PieceAt(over)) == 2) continue;
                exposed.Add(over);
                var promoted = Math.Abs(piece) == 1 && (piece == 1 ? landing < 4 : landing >= 28);
                if (!CanRecapture(position, attacker, over, landing, victimSide, promoted ? 2 : Math.Abs(piece))) undefended.Add(over);
                if (promoted) continue;
                var (lr, lc) = Rules.Coordinates(landing);
                foreach (var nextDr in Rules.Directions(piece))
                foreach (var nextDc in new[] { -1, 1 })
                {
                    var nextOver = Rules.Square(lr + nextDr, lc + nextDc);
                    var nextLanding = Rules.Square(lr + 2 * nextDr, lc + 2 * nextDc);
                    if (nextOver < 0 || nextOver == over || nextLanding < 0 || Math.Sign(position.PieceAt(nextOver)) != victimSide) continue;
                    if (Math.Abs(piece) == 1 && Math.Abs(position.PieceAt(nextOver)) == 2) continue;
                    if (position.PieceAt(nextLanding) != 0 && nextLanding != attacker && nextLanding != over) continue;
                    var nextPromotion = Math.Abs(piece) == 1 && (piece == 1 ? nextLanding < 4 : nextLanding >= 28);
                    continued = Math.Max(continued, (Math.Abs(position.PieceAt(nextOver)) == 2 ? 3 : 1) + (nextPromotion ? 2 : 0));
                }
            }
        }
        return (exposed.Sum(square => Math.Abs(position.PieceAt(square)) == 2 ? 3f : 1f),
            undefended.Sum(square => Math.Abs(position.PieceAt(square)) == 2 ? 3f : 1f), continued);
    }

    private static bool CanRecapture(BitPosition position, int attacker, int victim, int landing, int victimSide, int capturedRank)
    {
        for (var defender = 0; defender < 32; defender++)
        {
            var piece = position.PieceAt(defender);
            if (defender == victim || Math.Sign(piece) != victimSide || Math.Abs(piece) == 1 && capturedRank == 2) continue;
            var (row, column) = Rules.Coordinates(defender);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                if (Rules.Square(row + dr, column + dc) != landing) continue;
                var destination = Rules.Square(row + 2 * dr, column + 2 * dc);
                if (destination >= 0 && (position.PieceAt(destination) == 0 || destination == victim || destination == attacker)) return true;
            }
        }
        return false;
    }

    private static float Supported(BitPosition position, int side)
    {
        var value = 0f;
        for (var square = 0; square < 32; square++)
            if (Math.Sign(position.PieceAt(square)) == side && Near[square].Any(n => Math.Sign(position.PieceAt(n)) == side))
                value += Math.Abs(position.PieceAt(square)) == 2 ? 3f : 1f;
        return value;
    }

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
        foreach (var dr in new[] { -1, 1 })
        foreach (var dc in new[] { -1, 1 })
        {
            var attacker = Rules.Square(row - dr, column - dc);
            var landing = Rules.Square(row + dr, column + dc);
            if (attacker < 0 || landing < 0 || Math.Sign(board[attacker]) != -side) continue;
            if (!Rules.Directions(board[attacker]).Contains(dr)) continue;
            if (board[landing] == 0 || landing == origin) return true;
        }
        return false;
    }

    private static (float Exposed, float Undefended, float Continued) TacticalExposure(int[] board, int victimSide)
    {
        var exposed = new HashSet<int>();
        var undefended = new HashSet<int>();
        var continued = 0f;
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
                var promoted = Math.Abs(piece) == 1 && (piece == 1 ? landing < 4 : landing >= 28);
                if (!CanRecapture(board, attacker, over, landing, victimSide, promoted ? 2 : Math.Abs(piece))) undefended.Add(over);
                if (promoted) continue;
                var (lr, lc) = Rules.Coordinates(landing);
                foreach (var nextDr in Rules.Directions(piece))
                foreach (var nextDc in new[] { -1, 1 })
                {
                    var nextOver = Rules.Square(lr + nextDr, lc + nextDc);
                    var nextLanding = Rules.Square(lr + 2 * nextDr, lc + 2 * nextDc);
                    if (nextOver < 0 || nextOver == over || nextLanding < 0 || Math.Sign(board[nextOver]) != victimSide) continue;
                    if (Math.Abs(piece) == 1 && Math.Abs(board[nextOver]) == 2) continue;
                    if (board[nextLanding] != 0 && nextLanding != attacker && nextLanding != over) continue;
                    var nextPromotion = Math.Abs(piece) == 1 && (piece == 1 ? nextLanding < 4 : nextLanding >= 28);
                    continued = Math.Max(continued, (Math.Abs(board[nextOver]) == 2 ? 3 : 1) + (nextPromotion ? 2 : 0));
                }
            }
        }
        return (exposed.Sum(square => Math.Abs(board[square]) == 2 ? 3f : 1f),
            undefended.Sum(square => Math.Abs(board[square]) == 2 ? 3f : 1f), continued);
    }

    private static bool CanRecapture(int[] board, int attacker, int victim, int landing, int victimSide, int capturedRank)
    {
        for (var defender = 0; defender < 32; defender++)
        {
            var piece = board[defender];
            if (defender == victim || Math.Sign(piece) != victimSide || Math.Abs(piece) == 1 && capturedRank == 2) continue;
            var (row, column) = Rules.Coordinates(defender);
            foreach (var dr in Rules.Directions(piece))
            foreach (var dc in new[] { -1, 1 })
            {
                if (Rules.Square(row + dr, column + dc) != landing) continue;
                var destination = Rules.Square(row + 2 * dr, column + 2 * dc);
                if (destination >= 0 && (board[destination] == 0 || destination == victim || destination == attacker)) return true;
            }
        }
        return false;
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
