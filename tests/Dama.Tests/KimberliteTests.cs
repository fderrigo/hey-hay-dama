using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dama.Engine;
using Dama.Neural;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dama.Tests;

public sealed class KimberliteFactory : WebApplicationFactory<Dama.Neural.Program>;

public class KimberliteTests
{
    [Fact]
    public async Task OpeningUsesProtocolAndLoadsTheWrittenWeights()
    {
        using var factory = new KimberliteFactory();
        using var client = factory.CreateClient();
        var model = factory.Services.GetRequiredService<KimberliteModel>();
        Assert.Equal(JsonSerializer.Serialize(KimberliteWeightBuilder.Build(), NeuralNetwork.JsonOptions),
            JsonSerializer.Serialize(model.Network.Weights, NeuralNetwork.JsonOptions));
        var request = Request(Position.Initial());
        var response = await client.PostAsJsonAsync("/choose-move", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var answer = await response.Content.ReadFromJsonAsync<KimberliteResponse>();
        Assert.Equal("dama-model/v1", answer?.ProtocolVersion);
        Assert.Contains(answer?.MoveId, request.LegalMoves.Select(move => move.Id));
        Assert.True(answer?.Comment?.Length <= 240);
        var health = await client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", health.GetProperty("status").GetString());
    }

    [Fact]
    public async Task BlackMultiCaptureReturnsTheWholeOpaquePath()
    {
        using var factory = new KimberliteFactory();
        using var client = factory.CreateClient();
        var position = RulesTests.Board(-1, ("B3", -1), ("C4", 1), ("E6", 1));
        var request = Request(position, new KimberliteMove("21-14-7", 21, 7, [21, 14, 7], [18, 11], false));
        var response = await client.PostAsJsonAsync("/choose-move", request);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<KimberliteResponse>();
        Assert.Equal("21-14-7", answer?.MoveId);
    }

    [Fact]
    public async Task ConcurrentGamesDoNotSharePositionOrChoiceState()
    {
        using var factory = new KimberliteFactory();
        using var client = factory.CreateClient();
        var opening = Request(Position.Initial());
        var black = Request(RulesTests.Board(-1, ("B3", -1), ("C4", 1), ("E6", 1)),
            new KimberliteMove("21-14-7", 21, 7, [21, 14, 7], [18, 11], false));
        var tasks = Enumerable.Range(0, 12).Select(index => Check(client, index % 2 == 0 ? opening : black));
        Assert.All(await Task.WhenAll(tasks), Assert.True);

        static async Task<bool> Check(HttpClient client, KimberliteRequest request)
        {
            var response = await client.PostAsJsonAsync("/choose-move", request);
            var answer = await response.Content.ReadFromJsonAsync<KimberliteResponse>();
            return response.IsSuccessStatusCode && request.LegalMoves.Any(move => move.Id == answer?.MoveId);
        }
    }

    [Fact]
    public async Task InvalidProtocolAndJsonAreRejected()
    {
        using var factory = new KimberliteFactory();
        using var client = factory.CreateClient();
        var invalid = Request(Position.Initial()) with { ProtocolVersion = "v0" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/choose-move", invalid)).StatusCode);
        var malformed = await client.PostAsync("/choose-move", new StringContent("not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public void InferenceIsFastAndHonorsExternalCancellation()
    {
        var model = new KimberliteModel();
        var timer = Stopwatch.StartNew();
        var decision = model.Choose(Request(Position.Initial(), thinkTimeMs: 100));
        Assert.Contains(decision.MoveId, Rules.LegalMoves(Position.Initial()).Select(move => move.Id));
        Assert.True(timer.ElapsedMilliseconds < 1000);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => model.Choose(Request(Position.Initial()), cancelled.Token));
    }

    [Fact]
    public void EvaluationIsColourSymmetricAndImmediateWinIsSelected()
    {
        var model = new KimberliteModel();
        var position = RulesTests.Board(1, ("C4", 1), ("D5", -1));
        var decision = model.Choose(Request(position));
        Assert.Equal(Rules.LegalMoves(position).Single().Id, decision.MoveId);
        var symmetric = RulesTests.Board(1, ("D5", 1), ("F3", -2), ("B3", -1));
        var mirrored = new Position(symmetric.Board.Reverse().Select(piece => -piece).ToArray(), -symmetric.Turn);
        Assert.Equal(model.Network.Evaluate(symmetric, 1), model.Network.Evaluate(mirrored, -1), 6);
        Assert.Equal(model.Network.Evaluate(symmetric, 1), -model.Network.Evaluate(symmetric, -1), 6);
    }

    [Fact]
    public void ExplicitWeightsRoundTripAndContainNonlinearMotifs()
    {
        var weights = KimberliteWeightBuilder.Build();
        Assert.Equal(128, weights.InputSize);
        Assert.Equal(1626, weights.Hidden.Length);
        Assert.Equal(10210, new NeuralNetwork(weights).ParameterCount);
        var json = JsonSerializer.Serialize(weights, NeuralNetwork.JsonOptions);
        var restored = new NeuralNetwork(JsonSerializer.Deserialize<Dama.Neural.NetworkWeights>(json, NeuralNetwork.JsonOptions)!);
        var position = RulesTests.Board(1, ("D5", 1), ("E6", -1));
        Assert.Equal(new NeuralNetwork(weights).Evaluate(position, 1), restored.Evaluate(position, 1), 6);
        Assert.Contains(weights.Hidden, neuron => neuron.Feature == "man-en-prise" && neuron.Bias == -1 && neuron.OutputWeight < 0);
    }

    private static KimberliteRequest Request(Position position, KimberliteMove[]? legalMoves = null, int thinkTimeMs = 150)
    {
        var player = position.Turn == 1 ? "white" : "black";
        return new KimberliteRequest("dama-model/v1", "italian-draughts-8x8", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), player,
            new KimberlitePosition(position.Board, player, position.Ply, position.QuietKingPlyCount, 1),
            legalMoves ?? Rules.LegalMoves(position).Select(move => new KimberliteMove(move.Id, move.From + 1, move.To + 1,
                move.Path.Select(square => square + 1).ToArray(), move.Captures.Select(square => square + 1).ToArray(), move.Promotes)).ToArray(),
            [], thinkTimeMs);
    }

    private static KimberliteRequest Request(Position position, KimberliteMove move, int thinkTimeMs = 150) => Request(position, [move], thinkTimeMs);
}
