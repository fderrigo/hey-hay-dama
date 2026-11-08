using Dama.Engine;
using Diaspro;

namespace Diaspro.Tests;

public sealed class NetworkTests
{
    private static readonly Network Net = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", ManualWeights.Version + ".json"));

    private static int[] Board(params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return board;
    }

    [Fact]
    public void Shipped_checkpoint_matches_the_compiled_definition()
    {
        Assert.Equal(ManualWeights.Serialize(ManualWeights.Build()), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Models", ManualWeights.Version + ".json")));
        Assert.Equal(211, Net.InputCount);
        Assert.Equal(2309, Net.ParameterCount);
    }

    [Fact]
    public void Undefended_material_penalty_exceeds_material_value()
    {
        var weights = ManualWeights.Build().Coefficients;
        Assert.True(weights[10] * 112 + weights[12] + weights[17] > 112);
        Assert.True(weights[10] * 336 + 3 * (weights[12] + weights[17]) > 336);
    }

    [Fact]
    public void Colour_rotation_preserves_the_value()
    {
        var random = new Random(19);
        for (var test = 0; test < 250; test++)
        {
            var board = new int[32];
            for (var n = random.Next(2, 20); n > 0; n--) board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            var mirror = board.Reverse().Select(piece => -piece).ToArray();
            var difference = Math.Abs(Net.Evaluate(new Position(board)) - Net.Evaluate(new Position(mirror, -1)));
            Assert.True(difference < 0.01f, $"Differenza di rotazione {difference}");
        }
    }

    [Fact]
    public void Block_is_decisive()
    {
        var blocked = new Position(Board(("A4", 1), ("B5", -1), ("C6", -1)));
        Assert.Empty(Rules.LegalMoves(blocked));
        Assert.True(Net.Evaluate(blocked) < -1000);
    }

    [Fact]
    public void Policy_avoids_an_immediate_man_loss()
    {
        var position = new Position(Board(("D3", 1), ("H3", 1), ("D5", -1), ("B7", -1)));
        var legal = Rules.LegalMoves(position);
        var values = Policy.Values(Net, position, legal);
        var selected = legal[Policy.Choose(values)].Notation;
        Assert.NotEqual("22-18", selected);
        var exposed = Rules.Apply(position, legal.Single(m => m.Notation == "22-18"));
        var safe = Rules.Apply(position, legal.Single(m => m.Notation == "24-20"));
        var baseline = new Network(ManualWeights.Build() with { PatternSquare = null, PatternNear = null });
        Assert.True(Net.Evaluate(exposed) > Net.Evaluate(safe) + 80,
            $"Esposta {Net.Evaluate(exposed)}, sicura {Net.Evaluate(safe)}; senza pattern {baseline.Evaluate(exposed)}, {baseline.Evaluate(safe)}");
    }

    [Fact]
    public void Exposure_without_a_defender_is_counted()
    {
        var undefended = Net.Features(new Position(Board(("C4", 1), ("D5", -1))))[17];
        var defended = Net.Features(new Position(Board(("C4", 1), ("D5", -1), ("A2", 1))))[17];
        Assert.Equal(undefended + 1, defended);
    }

    [Fact]
    public void Available_recapture_removes_the_exposure_deficit()
    {
        var features = Net.Features(new Position(Board(("E4", 1), ("F5", -1), ("E2", 1))));
        var undefended = Net.Features(new Position(Board(("E4", 1), ("F5", -1))));
        Assert.Equal(undefended[17] + 1, features[17]);
    }

    [Fact]
    public void Double_capture_is_not_treated_as_a_simple_defended_exchange()
    {
        var features = Net.Features(new Position(Board(("E4", 1), ("F5", -1), ("E2", 1))));
        Assert.True(features[18] <= -3, $"Continuazione: {features[18]}");
    }

    [Fact]
    public void Man_cannot_recapture_a_king()
    {
        var man = Net.Features(new Position(Board(("E4", 1), ("F5", -1), ("E2", 1))))[17];
        var king = Net.Features(new Position(Board(("E4", 1), ("F5", -2), ("E2", 1))))[17];
        Assert.Equal(man - 2, king);
    }

    [Fact]
    public void Vacated_victim_square_can_enable_recapture()
    {
        var position = new Position(Board(("C4", 1), ("D5", -1), ("A2", 1)), -1);
        var capture = Assert.Single(Rules.LegalMoves(position));
        var reply = Assert.Single(Rules.LegalMoves(Rules.Apply(position, capture)));
        Assert.Equal(Rules.ParseSquare("A2"), reply.From);
        Assert.Equal(Rules.ParseSquare("C4"), reply.To);
        var undefended = Net.Features(new Position(Board(("C4", 1), ("D5", -1))))[17];
        Assert.Equal(undefended + 1, Net.Features(position with { Turn = 1 })[17]);
    }

    [Fact]
    public void Mandatory_double_capture_is_applied_as_one_complete_move()
    {
        var position = new Position(Board(("C2", 1), ("D3", -1), ("F5", -1), ("F7", -1)));
        var move = Assert.Single(Rules.LegalMoves(position));
        Assert.Equal(3, move.Captures.Length);
        var next = Rules.Apply(position, move);
        Assert.Equal(0, next.Board[Rules.ParseSquare("D3")]);
        Assert.Equal(0, next.Board[Rules.ParseSquare("F5")]);
        Assert.Equal(2, next.Board[Rules.ParseSquare("E8")]);
    }

    [Fact]
    public void Promotion_changes_the_rank_in_the_candidate_position()
    {
        var position = new Position(Board(("B7", 1)));
        var move = Rules.LegalMoves(position).First(m => m.To == Rules.ParseSquare("A8"));
        Assert.True(move.Promotes);
        Assert.Equal(2, Rules.Apply(position, move).Board[move.To]);
    }

    [Fact]
    public void Episodic_memory_is_per_game_and_does_not_change_weights()
    {
        var memory = new PositionMemory();
        var current = Position.Initial();
        var next = Rules.Apply(current, Rules.LegalMoves(current)[0]);
        Assert.Equal(0, memory.CountNext("one", next.Key));
        memory.Commit("one", current, next);
        Assert.Equal(1, memory.CountNext("one", next.Key));
        Assert.Equal(0, memory.CountNext("two", next.Key));
        var before = Net.Evaluate(next);
        Policy.Values(Net, current, Rules.LegalMoves(current), memory, "one");
        Assert.Equal(before, Net.Evaluate(next));
    }

    [Fact]
    public void Episodic_memory_does_not_count_a_chained_position_twice()
    {
        var memory = new PositionMemory();
        var first = Position.Initial();
        var second = Rules.Apply(first, Rules.LegalMoves(first)[0]);
        var third = Rules.Apply(second, Rules.LegalMoves(second)[0]);
        memory.Commit("chain", first, second);
        memory.Commit("chain", second, third);
        Assert.Equal(1, memory.CountNext("chain", first.Key));
        Assert.Equal(1, memory.CountNext("chain", second.Key));
        Assert.Equal(1, memory.CountNext("chain", third.Key));
    }

    [Fact]
    public void Episodic_memory_is_bounded_across_games()
    {
        var memory = new PositionMemory();
        var current = Position.Initial();
        var next = Rules.Apply(current, Rules.LegalMoves(current)[0]);
        for (var game = 0; game < 65; game++) memory.Commit($"game-{game}", current, next);
        Assert.Equal(64, memory.GameCount);
        Assert.Equal(0, memory.CountNext("game-0", next.Key));
        Assert.Equal(1, memory.CountNext("game-64", next.Key));
    }

    [Fact]
    public void Protocol_checks_the_complete_legal_transition()
    {
        var position = Position.Initial();
        var moves = Rules.LegalMoves(position).Select(m => new MoveDto(m.Id, m.From + 1, m.To + 1,
            m.Path.Select(s => s + 1).ToArray(), m.Captures.Select(s => s + 1).ToArray(), m.Promotes)).ToArray();
        var request = new ChooseMoveRequest(Protocol.Version, Protocol.RulesId, "r", "g", "white",
            new PositionDto(position.Board, "white", 0, 0, 1), moves, 1100);
        Assert.Null(Protocol.Validate(request));
        Assert.NotNull(Protocol.Validate(request with { ProtocolVersion = "bad" }));
        Assert.NotNull(Protocol.Validate(request with { LegalMoves = moves[..^1] }));
        Assert.Null(Protocol.Validate(request with { LegalMoves = [moves[0] with { Id = "opaque" }, .. moves[1..]] }));
    }

    [Fact]
    public void Evaluation_is_deterministic_under_concurrency()
    {
        var position = Position.Initial();
        var expected = Net.Evaluate(position);
        Parallel.For(0, 128, _ => Assert.Equal(expected, Net.Evaluate(position), 4));
    }
}
