using Dama.Engine;
using Klinamite;

namespace Klinamite.Tests;

public class NetworkTests
{
    private static readonly Network Net = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", "klinamite-v3.json"));

    private static int[] Board(params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return board;
    }

    private static float Value(Network network, int[] board) => network.Evaluate(board, 1);

    [Fact]
    public void Shipped_weights_match_the_manual_builder()
    {
        Assert.Equal(Network.Serialize(WeightBuilder.Build()), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Models", "klinamite-v3.json")));
        Assert.True(Net.NeuronCount > 500);
        Assert.True(Net.ConnectionCount > 2000);
    }

    [Fact]
    public void Rotation_and_colour_swap_preserve_value()
    {
        var random = new Random(5);
        for (var i = 0; i < 500; i++)
        {
            var board = new int[32];
            for (var n = random.Next(2, 20); n > 0; n--) board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            var mirror = new int[32];
            for (var square = 0; square < 32; square++) mirror[31 - square] = -board[square];
            Assert.Equal(Net.Evaluate(board, 1), Net.Evaluate(mirror, -1));
        }
    }

    [Fact]
    public void Initial_position_is_balanced_and_blocked_side_is_lost()
    {
        Assert.Equal(0, Net.Evaluate(Position.Initial().Board, 1));
        var blocked = Board(("A4", 1), ("B5", -1), ("C6", -1));
        Assert.Empty(Rules.LegalMoves(new Position(blocked)));
        Assert.True(Net.Evaluate(blocked, 1) < -10000);
    }

    [Fact]
    public void Man_does_not_detect_a_king_as_capturable()
    {
        var position = new Position(Board(("C4", 1), ("D5", -2)));
        Assert.DoesNotContain(Rules.LegalMoves(position), m => m.Captures.Length > 0);
        var man = Value(Net, Board(("C4", 1), ("D5", -1)));
        var king = Value(Net, Board(("C4", 1), ("D5", -2)));
        Assert.True(king < man - 250, $"man {man}, king {king}");
    }

    [Fact]
    public void Two_attackers_on_one_victim_do_not_double_the_capture_value()
    {
        var attacked = Value(Net, Board(("B3", 1), ("D3", 1), ("C4", -1)));
        var quiet = Value(Net, Board(("B3", 1), ("D3", 1), ("F5", -1)));
        Assert.InRange(attacked - quiet, 50, 130);
    }

    [Fact]
    public void Alternative_victims_use_maximum_but_a_real_double_counts_twice()
    {
        var alternatives = Value(Net, Board(("B3", 1), ("H3", 1), ("C4", -1), ("G4", -1)));
        var one = Value(Net, Board(("B3", 1), ("H3", 1), ("C4", -1), ("F5", -1)));
        Assert.True(Math.Abs(alternatives - one) < 30, $"alternatives {alternatives}, one {one}");
        var realDouble = Value(Net, Board(("B3", 1), ("C4", -1), ("E6", -1)));
        var single = Value(Net, Board(("B3", 1), ("C4", -1), ("F5", -1)));
        Assert.True(realDouble > single + 60, $"double {realDouble}, single {single}");
    }

    [Fact]
    public void Mixed_alternatives_do_not_sum_and_a_mixed_double_has_full_value()
    {
        var alternatives = Value(Net, Board(("B3", 2), ("H3", 2), ("C4", -1), ("G4", -2)));
        var one = Value(Net, Board(("B3", 2), ("H3", 2), ("C6", -1), ("G4", -2)));
        Assert.True(Math.Abs(alternatives - one) < 30, $"alternatives {alternatives}, one {one}");
        var realDouble = Value(Net, Board(("B3", 2), ("C4", -1), ("E6", -2)));
        var single = Value(Net, Board(("B3", 2), ("C4", -1), ("F5", -2)));
        Assert.True(realDouble > single + 100, $"double {realDouble}, single {single}");
    }

    [Fact]
    public void Real_triples_and_mixed_king_triples_have_full_value()
    {
        var position = new Position(Board(("C2", 1), ("D3", -1), ("F5", -1), ("F7", -1)));
        var move = Assert.Single(Rules.LegalMoves(position));
        Assert.Equal(3, move.Captures.Length);
        Assert.Equal("26x19x12x3", move.Notation);
        var candidate = new Candidate(move.From, move.To, move.Captures, move.Promotes);
        Assert.Equal(Rules.Apply(position, move).Board, Policy.After(position.Board, candidate));
        var triple = Value(Net, position.Board);
        var onlyDouble = Value(Net, Board(("C2", 1), ("D3", -1), ("F5", -1), ("H7", -1)));
        Assert.True(triple > onlyDouble + 60, $"triple {triple}, double {onlyDouble}");

        var mixedTriple = Value(Net, Board(("C2", 2), ("D3", -1), ("F5", -2), ("F7", -1)));
        var mixedDouble = Value(Net, Board(("C2", 2), ("D3", -1), ("F5", -2), ("H7", -1)));
        Assert.True(mixedTriple > mixedDouble + 60, $"triple {mixedTriple}, double {mixedDouble}");
    }

    [Fact]
    public void King_victim_stops_a_mans_double_capture()
    {
        var position = new Position(Board(("B3", 1), ("C4", -1), ("E6", -2)));
        var legal = Rules.LegalMoves(position);
        Assert.Single(legal);
        Assert.Single(legal[0].Captures);
    }

    [Fact]
    public void Vacated_origin_enables_and_is_priced_as_a_double_recapture()
    {
        var position = new Position(Board(("B5", 1), ("C4", 1), ("D3", 1), ("A6", -1), ("D5", -1)));
        var capture = Assert.Single(Rules.LegalMoves(position));
        var next = Rules.Apply(position, capture);
        var reply = Assert.Single(Rules.LegalMoves(next));
        Assert.Equal(2, reply.Captures.Length);
        var candidate = new Candidate(capture.From, capture.To, capture.Captures, capture.Promotes);
        Assert.Equal(next.Board, Policy.After(position.Board, candidate));
        var doubleValue = Net.Evaluate(next.Board, next.Turn);
        var noDouble = Net.Evaluate(Board(("B5", 1), ("E6", 1), ("F3", 1), ("A6", -1)), next.Turn);
        Assert.True(doubleValue > noDouble + 60, $"double {doubleValue}, single {noDouble}");
    }

    [Fact]
    public void Promotion_is_applied_to_the_candidate_board()
    {
        var board = Board(("B7", 1));
        var promoted = Policy.After(board, new Candidate(Rules.ParseSquare("B7"), Rules.ParseSquare("A8"), [], true));
        Assert.Equal(2, promoted[Rules.ParseSquare("A8")]);
        Assert.Equal(0, promoted[Rules.ParseSquare("B7")]);
    }

    [Fact]
    public void Policy_prefers_the_move_that_leaves_no_man_hanging()
    {
        var position = new Position(Board(("D3", 1), ("H3", 1), ("D5", -1), ("B7", -1)));
        var legal = Rules.LegalMoves(position);
        var candidates = legal.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
        var values = Policy.Values(Net, position.Board, 1, candidates);
        var byMove = legal.Select((m, i) => (m.Notation, Value: values[i])).ToDictionary(x => x.Notation, x => x.Value);
        Assert.True(byMove["24-20"] > byMove["22-18"], $"safe {byMove["24-20"]}, exposed {byMove["22-18"]}");
        Assert.True(byMove["24-20"] > byMove["22-19"]);
    }

    [Fact]
    public void Immediate_king_returns_are_discouraged_in_two_recorded_cycles()
    {
        var first = new Position([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 2, 2, -1, -1, -2, 0, 1, -1, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0]);
        var firstMoves = Rules.LegalMoves(first);
        var firstCandidates = firstMoves.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
        RecentMoveDto[] firstHistory = [new(81, "white", "10-13"), new(82, "black", "21-17")];
        var firstBase = Policy.Values(Net, first.Board, 1, firstCandidates);
        var firstValues = Policy.Values(Net, first.Board, 1, firstCandidates, firstHistory, "white");
        var firstReturn = firstMoves.FindIndex(m => m.Notation == "13-10");
        Assert.Equal(5, firstBase[firstReturn] - firstValues[firstReturn]);
        Assert.Equal("13-18", firstMoves[Policy.Choose(firstValues)].Notation);

        var second = new Position([0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, -2, 0, 2, 0, -2, -2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], -1);
        var secondMoves = Rules.LegalMoves(second);
        var secondCandidates = secondMoves.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
        RecentMoveDto[] secondHistory = [new(88, "black", "22-18"), new(89, "white", "20-16")];
        var secondBase = Policy.Values(Net, second.Board, -1, secondCandidates);
        var secondValues = Policy.Values(Net, second.Board, -1, secondCandidates, secondHistory, "black");
        var secondReturn = secondMoves.FindIndex(m => m.Notation == "18-22");
        Assert.Equal(5, secondBase[secondReturn] - secondValues[secondReturn]);
        Assert.NotEqual("18-22", secondMoves[Policy.Choose(secondValues)].Notation);
    }

    [Fact]
    public void Return_penalty_stays_disabled_when_the_chooser_is_behind()
    {
        var position = new Position([0, 0, -1, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, -2, 0, 0, 0, 0, -2, 0, 0, 0, 0, 0, 0, 0, -2, 0, 0, 0, 0, 0]);
        var legal = Rules.LegalMoves(position);
        var candidates = legal.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
        RecentMoveDto[] history = [new(133, "white", "13-10"), new(134, "black", "11-14")];
        var baseline = Policy.Values(Net, position.Board, 1, candidates);
        var values = Policy.Values(Net, position.Board, 1, candidates, history, "white");
        Assert.Equal(baseline, values);
        Assert.Single(values.Distinct());
        Assert.Equal("10-13", legal[Policy.Choose(values)].Notation);
    }

    [Fact]
    public void Missing_or_opaque_history_has_no_effect()
    {
        var position = new Position([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 2, 2, -1, -1, -2, 0, 1, -1, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0]);
        var legal = Rules.LegalMoves(position);
        var candidates = legal.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
        var baseline = Policy.Values(Net, position.Board, 1, candidates);
        RecentMoveDto[] opaque = [new(81, "white", "opaque-A"), new(82, "black", "opaque-B")];
        Assert.Equal(baseline, Policy.Values(Net, position.Board, 1, candidates, opaque, "white"));
        Assert.Equal(baseline, Policy.Values(Net, position.Board, 1, candidates, [], "white"));
    }

    [Fact]
    public void Candidate_application_and_protocol_validation_match_the_contract()
    {
        var position = Position.Initial();
        var move = Rules.LegalMoves(position).First(m => m.Notation == "22-18");
        var candidate = new Candidate(move.From, move.To, move.Captures, move.Promotes);
        Assert.Equal(Rules.Apply(position, move).Board, Policy.After(position.Board, candidate));
        var request = new ChooseMoveRequest(Protocol.Version, Protocol.RulesId, "r", "g", "white",
            new PositionDto(position.Board, "white", 0, 0, 1),
            [new MoveDto(move.Notation, move.From + 1, move.To + 1, move.Path.Select(s => s + 1).ToArray(), [], false)], 1000);
        Assert.Null(Protocol.Validate(request));
        Assert.Equal("protocolVersion non supportata.", Protocol.Validate(request with { ProtocolVersion = "bad" }));
    }
}
