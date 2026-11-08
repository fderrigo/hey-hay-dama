using System.Net;
using System.Net.Http.Json;
using Dama.Engine;
using Dama.Web;
using Microsoft.Extensions.Configuration;

namespace Dama.Tests;

// Protocol fixtures only: no game, room, or competition record is created.
public class ModelTransportTests
{
    private sealed class DelayedHandler : HttpMessageHandler
    {
        public ModelRequest? Received { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Received = await request.Content!.ReadFromJsonAsync<ModelRequest>(cancellationToken);
            await Task.Delay(250, cancellationToken);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ModelResponse("dama-model/v1", Received!.LegalMoves[0].Id, null)) };
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static ModelRegistry Registry(int grace, HttpClient client)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Arena:Models:0:Id"] = "transport-fixture",
            ["Arena:Models:0:Name"] = "Protocol fixture",
            ["Arena:Models:0:Kind"] = "http",
            ["Arena:Models:0:Endpoint"] = "http://fixture.invalid/choose-move",
            ["Arena:Models:0:MaxThinkTimeMs"] = "100",
            ["Arena:Models:0:HttpGraceMs"] = grace.ToString()
        }).Build();
        return new(config, null!, new Factory(client));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1000, true)]
    public async Task GraceAllowsTransportDelayWithoutIncreasingEngineBudget(int grace, bool succeeds)
    {
        using var handler = new DelayedHandler();
        using var client = new HttpClient(handler);
        var registry = Registry(grace, client);
        var position = Position.Initial();
        var legal = Rules.LegalMoves(position);
        Task<ModelChoice> Choose() => registry.Choose("transport-fixture", "fixture", position, 1, [], legal, CancellationToken.None);
        if (succeeds)
        {
            Assert.Equal(legal[0].Id, (await Choose()).Move.Id);
            Assert.Equal(100, handler.Received!.ThinkTimeMs);
        }
        else await Assert.ThrowsAsync<ModelFailure>(Choose);
    }

    [Fact]
    public async Task CallerCancellationStillStopsRequestWithGrace()
    {
        using var handler = new DelayedHandler();
        using var client = new HttpClient(handler);
        var registry = Registry(1000, client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var position = Position.Initial();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.Choose("transport-fixture", "fixture", position, 1, [], Rules.LegalMoves(position), cancellation.Token));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5001)]
    public void InvalidGraceIsRejected(int grace)
    {
        using var client = new HttpClient();
        Assert.Throws<InvalidOperationException>(() => Registry(grace, client));
    }
}
