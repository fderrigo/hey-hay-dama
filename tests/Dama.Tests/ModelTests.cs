using System.Text.Json;
using Dama.Engine;

namespace Dama.Tests;

public class ModelTests
{
    private readonly NeuralModel network = new(WeightBuilder.Build());

    [Fact] public void PublishedWeightArtifactMatchesTheConstructedNetwork()
    {
        var published = NeuralModel.Load(Path.Combine(AppContext.BaseDirectory, "Models", "opale-v1.json"));
        Assert.Equal(JsonSerializer.Serialize(network.Weights, NeuralModel.JsonOptions), JsonSerializer.Serialize(published.Weights, NeuralModel.JsonOptions));
    }

    [Fact] public void ExportReloadPreservesDecisionsAndIsDeterministic()
    {
        var json = JsonSerializer.Serialize(network.Weights, NeuralModel.JsonOptions);
        Assert.Equal(json, JsonSerializer.Serialize(WeightBuilder.Build(), NeuralModel.JsonOptions));
        var restored = new NeuralModel(JsonSerializer.Deserialize<NetworkWeights>(json, NeuralModel.JsonOptions)!);
        var p = RulesTests.Board(1, ("B3", 1), ("F3", 1), ("D5", -1), ("G6", -2));
        Assert.Equal(network.Evaluate(p, 1), restored.Evaluate(p, 1));
        Assert.Equal(network.Choose(p, Rules.LegalMoves(p)).Move.Id, restored.Choose(p, Rules.LegalMoves(p)).Move.Id);
        Assert.Equal(1080, network.Weights.Hidden.Length); Assert.Equal(6412, network.ParameterCount);
    }
    [Fact] public void EvaluationIsColourSymmetricAndKingsAreValuable()
    {
        var p = RulesTests.Board(1, ("B3", 1), ("F3", 1), ("D5", -1), ("G6", -2));
        var mirrored = new Position(p.Board.Reverse().Select(x => -x).ToArray(), -p.Turn);
        Assert.Equal(network.Evaluate(p, 1), network.Evaluate(mirrored, -1), 6);
        Assert.Equal(network.Evaluate(p, 1), -network.Evaluate(p, -1), 6);
        Assert.True(network.Evaluate(RulesTests.Board(1, ("D5", 2)), 1) > network.Evaluate(RulesTests.Board(1, ("D5", 1)), 1));
    }
    [Fact] public void SearchAndNetworkChooseLegalMovesAndImmediateWin()
    {
        var p = RulesTests.Board(1, ("C4", 1), ("D5", -1));
        var legal = Rules.LegalMoves(p);
        var pure = network.Choose(p, legal);
        var search = new SearchModel(network).Choose(p, legal, 6, 100);
        Assert.Equal(legal.Single().Id, pure.Move.Id);
        Assert.Equal(legal.Single().Id, search.Move.Id);
        Assert.True(pure.Score > 99000); Assert.True(search.Score > 99000);
    }
    [Fact] public void NetworkDetectsJumpMotifsNonlinearly()
    {
        var unsafePosition = RulesTests.Board(1, ("D5", 1), ("E6", -1));
        var blockedLanding = RulesTests.Board(1, ("D5", 1), ("E6", -1), ("F7", 1));
        var standalone = RulesTests.Board(1, ("F7", 1));
        // The extra piece has a context-dependent contribution: hidden ReLU motifs are not a linear material sum.
        var delta = network.Evaluate(blockedLanding, 1) - network.Evaluate(unsafePosition, 1);
        Assert.NotEqual(network.Evaluate(standalone, 1), delta);
    }
}
