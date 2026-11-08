using Dama.Competition;
using Dama.Engine;
using Agata;
using WeightBuilder;
using EngineNetwork = Agata.Network;

namespace Agata.Tests;

public class NetworkTests
{
    private static readonly string WeightsPath = Path.Combine(AppContext.BaseDirectory, "Models", "agata-v1.json");
    private static readonly EngineNetwork Net = EngineNetwork.Load(WeightsPath);

    private static int[] Board(params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return board;
    }

    [Fact]
    public void Shipped_weights_are_exactly_what_the_builder_writes() =>
        Assert.Equal(NetworkFile.Serialize(NetworkBuilder.Build()), File.ReadAllBytes(WeightsPath));

    [Fact]
    public void Service_weights_are_exactly_what_the_builder_writes() =>
        Assert.Equal(NetworkFile.Serialize(NetworkBuilder.BuildV9()), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Models", "agata-v9.json")));

    [Fact]
    public void Network_is_feed_forward_and_loads_from_the_file()
    {
        Assert.True(Net.NeuronCount > 1000);
        Assert.True(Net.ConnectionCount > 5000);
        var definition = NetworkBuilder.Build();
        Assert.All(definition.Neurons.Select((n, i) => (n, id: definition.Inputs + i)), x => Assert.All(x.n.From, f => Assert.True(f < x.id)));
    }

    [Fact]
    public void Value_is_unchanged_by_rotating_the_board_and_swapping_colours()
    {
        var random = new Random(5);
        for (var i = 0; i < 5000; i++)
        {
            var board = new int[32];
            for (var n = random.Next(2, 22); n > 0; n--) board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
            var mirror = new int[32];
            for (var sq = 0; sq < 32; sq++) mirror[31 - sq] = -board[sq];
            Assert.Equal(Net.Evaluate(board, 1), Net.Evaluate(mirror, -1));
        }
    }

    [Fact]
    public void Initial_position_is_balanced_except_for_the_tempo_of_having_no_threats()
    {
        Assert.InRange(Net.Evaluate(Position.Initial().Board, 1), -5f, 5f);
    }

    [Fact]
    public void Extra_man_and_a_king_over_a_man_are_valued_as_such()
    {
        var level = Net.Evaluate(Board(("D3", 1), ("E6", -1)), 1);
        var up = Net.Evaluate(Board(("D3", 1), ("F3", 1), ("E6", -1)), 1);
        var promoted = Net.Evaluate(Board(("D3", 2), ("E6", -1)), 1);
        Assert.True(up > level + 80);
        Assert.True(promoted > level + 40);
    }

    [Fact]
    public void Side_with_no_legal_move_is_seen_as_lost()
    {
        // White's man on A8 cannot exist; a blocked man and no pieces at all are the two real cases.
        var blocked = Board(("A4", 1), ("B5", -1), ("C6", -1));
        Assert.Empty(Rules.LegalMoves(new Position(blocked)));
        Assert.True(Net.Evaluate(blocked, 1) < -10_000);
        Assert.True(Net.Evaluate(Board(("E6", -1)), 1) < -10_000);
        Assert.True(Net.Evaluate(Position.Initial().Board, 1) > -10_000);
    }

    [Fact]
    public void Available_capture_is_valued_and_a_recapture_reduces_it()
    {
        // D3 can jump E4 and land on F5. With H7 held, a man on G6 cannot be jumped in turn and recaptures.
        var safe = Net.Evaluate(Board(("D3", 1), ("E4", -1), ("A6", -1), ("H7", -1)), 1);
        var answered = Net.Evaluate(Board(("D3", 1), ("E4", -1), ("G6", -1), ("H7", -1)), 1);
        var quiet = Net.Evaluate(Board(("D3", 1), ("E6", -1), ("A6", -1), ("H7", -1)), 1);
        Assert.True(safe > quiet + 40, $"safe {safe}, quiet {quiet}");
        Assert.True(safe > answered + 30, $"safe {safe}, answered {answered}");
    }

    private static EngineNetwork V3() => new(System.Text.Json.JsonSerializer.Deserialize<Agata.NetworkDefinition>(
        NetworkFile.Serialize(NetworkBuilder.BuildV3()), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);

    [Fact]
    public void V3_sees_the_double_opened_by_the_square_a_forced_capture_leaves()
    {
        // C4xD5 lands safely on E6, then A6xB5xD3 runs through the C4 that capture just emptied.
        var trap = Board(("B5", 1), ("C4", 1), ("D3", 1), ("A6", -1), ("D5", -1));
        var p = new Position(trap);
        var only = Assert.Single(Rules.LegalMoves(p));
        Assert.Equal("C4", Rules.Algebraic(only.From));
        Assert.Equal(2, Rules.LegalMoves(Rules.Apply(p, only)).Max(m => m.Captures.Length));
        Assert.True(Net.Evaluate(trap, 1) > 50, "v1 rates it as a win of a man");
        Assert.True(V3().Evaluate(trap, 1) < 0, "v3 must rate it as losing");
    }

    [Fact]
    public void V3_counts_alternatives_and_the_longest_path_not_their_sum()
    {
        var net = V3();
        // Two men attack one victim, or one man attacks two: neither is worth more than one capture.
        var quiet = net.Evaluate(Board(("B3", 1), ("D3", 1), ("E6", -1), ("A6", -1)), 1);
        var oneVictim = net.Evaluate(Board(("B3", 1), ("D3", 1), ("C4", -1), ("A6", -1)), 1);
        var twoAlternatives = net.Evaluate(Board(("B3", 1), ("H3", 1), ("C4", -1), ("G4", -1)), 1);
        var chain = net.Evaluate(Board(("B3", 1), ("D3", 1), ("C4", -1), ("E6", -1)), 1);
        Assert.InRange(oneVictim - quiet, 30, 180);
        Assert.InRange(twoAlternatives - quiet, 30, 180);
        Assert.InRange(chain - quiet, 130, 320);
    }

    private static EngineNetwork V4() => new(System.Text.Json.JsonSerializer.Deserialize<Agata.NetworkDefinition>(
        NetworkFile.Serialize(NetworkBuilder.BuildV4()), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);

    [Fact]
    public void V4_sees_a_recapture_that_runs_on_through_the_landing_square()
    {
        // D3xE4 lands on F5 (D5 stops F3 taking, H7 stops a second jump); G6xF5 stops on E4 and takes F3, ending on G2.
        var trap = Board(("D3", 1), ("F3", 1), ("E4", -1), ("G6", -1), ("H7", -1), ("D5", -1));
        var p = new Position(trap);
        var only = Assert.Single(Rules.LegalMoves(p));
        Assert.Equal("D3", Rules.Algebraic(only.From));
        Assert.Equal(2, Rules.LegalMoves(Rules.Apply(p, only)).Max(m => m.Captures.Length));
        Assert.True(V4().Evaluate(trap, 1) < V3().Evaluate(trap, 1) - 100);
    }

    [Fact]
    public void V4_values_a_man_about_to_promote_only_when_the_square_is_free()
    {
        var (v3, v4) = (V3(), V4());
        int[] open = Board(("B7", 1), ("E2", 1), ("G6", -1), ("A6", -1)), shut = Board(("B7", 1), ("E2", 1), ("A8", -1), ("C8", -1));
        var added = v4.Evaluate(open, 1) - v3.Evaluate(open, 1);
        var none = v4.Evaluate(shut, 1) - v3.Evaluate(shut, 1);
        Assert.True(added > none + 30, $"added {added}, none {none}");
    }

    [Fact]
    public void V5_does_not_call_a_trap_a_capture_that_carries_on()
    {
        var v5 = new EngineNetwork(System.Text.Json.JsonSerializer.Deserialize<Agata.NetworkDefinition>(
            NetworkFile.Serialize(NetworkBuilder.BuildV5()), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
        // As the trap fixture, but C4xD5 continues over F7 to G8, so it wins two men.
        var carriesOn = Board(("B5", 1), ("C4", 1), ("D3", 1), ("A6", -1), ("D5", -1), ("F7", -1));
        Assert.Equal(2, Assert.Single(Rules.LegalMoves(new Position(carriesOn))).Captures.Length);
        Assert.True(v5.Evaluate(carriesOn, 1) > V3().Evaluate(carriesOn, 1) + 100);
        var trap = Board(("B5", 1), ("C4", 1), ("D3", 1), ("A6", -1), ("D5", -1));
        Assert.Equal(V3().Evaluate(trap, 1), v5.Evaluate(trap, 1));
    }

    [Fact]
    public void V6_prefers_trades_only_when_ahead()
    {
        var v6 = new EngineNetwork(System.Text.Json.JsonSerializer.Deserialize<Agata.NetworkDefinition>(
            NetworkFile.Serialize(NetworkBuilder.BuildV6()), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
        float Added(int[] board) => v6.Evaluate(board, 1) - V3().Evaluate(board, 1);
        // Three pieces against two, two against one, and level at two each.
        Assert.Equal(75, Added(Board(("B1", 1), ("D1", 1), ("F1", 1), ("A8", -1), ("C8", -1))), 1);
        Assert.Equal(100, Added(Board(("B1", 1), ("D1", 1), ("A8", -1))), 1);
        Assert.Equal(0, Added(Board(("B1", 1), ("D1", 1), ("A8", -1), ("C8", -1))), 1);
        // The other side's lead counts against the side to move.
        Assert.Equal(-100, Added(Board(("B1", 1), ("A8", -1), ("C8", -1))), 1);
    }

    [Fact]
    public void V7_makes_an_offered_trade_good_only_for_the_side_ahead()
    {
        var v7 = new EngineNetwork(System.Text.Json.JsonSerializer.Deserialize<Agata.NetworkDefinition>(
            NetworkFile.Serialize(NetworkBuilder.BuildV7()), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
        float Added(int[] board) => v7.Evaluate(board, 1) - V3().Evaluate(board, 1);
        // D3xE4 lands on F5 and G6 retakes (H7 held). Black leads three to one.
        Assert.Equal(-65, Added(Board(("D3", 1), ("E4", -1), ("G6", -1), ("H7", -1))), 1);
        // Same capture with the pieces level: no change.
        Assert.Equal(0, Added(Board(("D3", 1), ("B1", 1), ("D1", 1), ("E4", -1), ("G6", -1), ("H7", -1))), 1);
        // A safe capture is not a trade, even with Black ahead.
        Assert.Equal(0, Added(Board(("D3", 1), ("E4", -1), ("A6", -1), ("H7", -1))), 1);
    }

    private static EngineNetwork Built(WeightBuilder.NetworkDefinition definition) => new(System.Text.Json.JsonSerializer.Deserialize<Agata.NetworkDefinition>(
        NetworkFile.Serialize(definition), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);

    public static TheoryData<string, string> Crowns() => new() { { "B7", "A8" }, { "B7", "C8" }, { "D7", "C8" }, { "D7", "E8" }, { "F7", "E8" }, { "F7", "G8" }, { "H7", "G8" } };

    [Theory]
    [MemberData(nameof(Crowns))]
    public void V8_promotion_gains_at_least_a_quarter_of_a_man_in_every_phase(string man, string crown)
    {
        var v8 = Built(NetworkBuilder.BuildV8());
        (string, int)[][] scenes =
        [
            [("B1", -2)],
            [("B1", -2), ("B3", 1), ("D3", 1), ("F3", 1), ("H3", 1), ("D1", 1), ("F1", 1), ("H1", 1), ("B5", -1)],
            [("B1", -2), ("B3", 1), ("D3", 1), ("F3", 1), ("H3", 1), ("D1", 1), ("F1", 1), ("H1", 1), ("A2", 1), ("C2", 1), ("E2", 1), ("G2", 1), ("B5", -1), ("D5", -1), ("F5", -1), ("H5", -1)],
        ];
        foreach (var scene in scenes)
        {
            var before = Board([.. scene, (man, 1)]);
            var after = Board([.. scene, (crown, 2)]);
            Assert.True(v8.Evaluate(before, -1) - v8.Evaluate(after, -1) >= 25, $"{man}-{crown} with {scene.Length} others");
        }
    }

    [Fact]
    public void V8_prefers_promoting_in_the_ending_v7_only_drew()
    {
        var v8 = Built(NetworkBuilder.BuildV8());
        (string, int)[] scene = [("B7", 2), ("E4", -2)];
        Assert.True(v8.Evaluate(Board([.. scene, ("G8", 2)]), -1) < v8.Evaluate(Board([.. scene, ("H7", 1)]), -1));
        Assert.True(V8Old().Evaluate(Board([.. scene, ("G8", 2)]), -1) > V8Old().Evaluate(Board([.. scene, ("H7", 1)]), -1));
    }

    private static EngineNetwork V8Old() => Built(NetworkBuilder.BuildV7());

    [Fact]
    public void V9_prices_what_a_capture_takes()
    {
        var (v8, v9) = (Built(NetworkBuilder.BuildV8()), Built(NetworkBuilder.BuildV9()));
        float Added(int[] board) => v9.Evaluate(board, 1) - v8.Evaluate(board, 1);
        // C6xD7 lands on the last row.
        Assert.Equal(70, Added(Board(("C6", 1), ("D7", -1))), 1);
        // Same capture lower down: nothing extra.
        Assert.Equal(0, Added(Board(("C4", 1), ("D5", -1))), 1);
        // Kings face each other: the side to move takes one, the other only threatens.
        Assert.Equal(55, Added(Board(("D3", 2), ("E4", -2))), 1);
        // A king taking a man is plain capture.
        Assert.Equal(0, Added(Board(("D3", 2), ("E4", -1))), 1);
    }

    [Fact]
    public void V4_stays_under_the_size_limit() => Assert.True(V4().NeuronCount - EngineNetwork.InputCount < 3000);

    [Fact]
    public void V3_stays_under_the_size_limit() => Assert.True(V3().NeuronCount - EngineNetwork.InputCount < 3000);

    [Fact]
    public void Network_prefers_the_move_that_leaves_no_man_hanging()
    {
        // D3-C4 and D3-E4 both let the man on D5 jump; H3-G4 gives nothing away.
        var p = new Position(Board(("D3", 1), ("H3", 1), ("D5", -1), ("B7", -1)));
        var legal = Rules.LegalMoves(p);
        var candidates = legal.Select(m => new Candidate(m.From, m.To, m.Captures, m.Promotes)).ToArray();
        var values = Policy.Values(Net, p.Board, 1, candidates);
        var value = legal.Select((m, i) => (m.Notation, Value: values[i])).ToDictionary(x => x.Notation, x => x.Value);
        Assert.True(value["24-20"] > value["22-18"]);
        Assert.True(value["24-20"] > value["22-19"]);
    }

    [Fact]
    public void Softmax_mostly_keeps_the_best_and_is_repeatable_with_a_seed()
    {
        float[] values = [0, 5, 300];
        Assert.Equal(2, Policy.Choose(values, 0, new Random(1)));
        var picks = Enumerable.Range(0, 200).Select(i => Policy.Choose(values, 10, new Random(i))).ToArray();
        Assert.All(picks, p => Assert.Equal(2, p));

        float[] close = [0, 4];
        var mixed = Enumerable.Range(0, 400).Select(i => Policy.Choose(close, 10, new Random(i))).Distinct().Count();
        Assert.Equal(2, mixed);
        Assert.Equal(Policy.Choose(close, 10, new Random(7)), Policy.Choose(close, 10, new Random(7)));
    }

    [Fact]
    public void Applying_a_candidate_matches_the_game_engine()
    {
        var random = new Random(11);
        var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
        for (var game = 0; game < 200; game++)
        {
            var p = Position.Initial();
            var seen = new Dictionary<string, int> { [p.Key] = 1 };
            Outcome? outcome = null;
            var player = Participant.Ai("reference-random", "Casuale");
            using var tracked = competition.Start("test", player, player);
            for (var ply = 0; ply < 150; ply++)
            {
                var legal = Rules.LegalMoves(p);
                if (legal.Count == 0) break;
                var m = legal[random.Next(legal.Count)];
                var next = Rules.Apply(p, m);
                Assert.Equal(next.Board, Policy.After(p.Board, new Candidate(m.From, m.To, m.Captures, m.Promotes)));
                p = next;
                seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
                outcome = Rules.GetOutcome(p, seen[p.Key]);
                if (outcome is not null) break;
            }
            if (outcome is not null) tracked.Complete(outcome.Winner, p.Ply, outcome.Reason);
            else tracked.Interrupt(p.Ply, "test-ply-limit");
        }
    }
}
