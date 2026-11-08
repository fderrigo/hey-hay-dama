using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dama.Web;
using Dama.Competition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dama.Tests;

public sealed class GameFactory : WebApplicationFactory<Program>
{
    public readonly StubHandler Stub = new();
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "dama-tests", Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Arena:DataDirectory"] = DataDirectory,
            ["Arena:DefaultMatchSource"] = "test",
            ["Arena:Models:2:Id"] = "external-test", ["Arena:Models:2:Name"] = "External test",
            ["Arena:Models:2:Kind"] = "http", ["Arena:Models:2:Endpoint"] = "http://model.test/choose-move",
            ["Arena:Models:2:MaxThinkTimeMs"] = "100"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StubFactory(Stub));
        });
    }
    private sealed class StubFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
}
public sealed class StubHandler : HttpMessageHandler
{
    public string Mode { get; set; } = "valid";
    public ModelRequest? LastRequest { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = await request.Content!.ReadFromJsonAsync<ModelRequest>(cancellationToken);
        if (Mode == "timeout") await Task.Delay(5000, cancellationToken);
        var json = Mode switch
        {
            "illegal" => "{\"protocolVersion\":\"dama-model/v1\",\"moveId\":\"1-32\"}",
            "version" => "{\"protocolVersion\":\"v0\",\"moveId\":\"21-17\"}",
            "json" => "not json",
            "large" => new string('x', 16385),
            _ => JsonSerializer.Serialize(new ModelResponse("dama-model/v1", LastRequest!.LegalMoves[0].Id, "<script>untrusted</script>"), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

public class ApiTests
{
    private static async Task<(string Id, string Token, JsonElement Game)> Create(HttpClient client, string white = "human", string black = "opale", string access = "online")
    {
        var response = await client.PostAsJsonAsync("/api/games", new CreateGame(white, black, access));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("game").GetProperty("id").GetString()!, json.GetProperty("token").GetString()!, json.GetProperty("game"));
    }
    private static void Authenticate(HttpClient client, string token) { client.DefaultRequestHeaders.Remove("X-Game-Token"); client.DefaultRequestHeaders.Add("X-Game-Token", token); }

    [Fact] public async Task HumanModelGameValidatesTurnsAndCanUndo()
    {
        using var factory = new GameFactory(); using var client = factory.CreateClient();
        var (id, token, _) = await Create(client); Authenticate(client, token);
        var illegal = await client.PostAsJsonAsync($"/api/games/{id}/move", new TurnRequest(0, "1-32"));
        Assert.Equal(HttpStatusCode.BadRequest, illegal.StatusCode);
        (await client.PostAsJsonAsync($"/api/games/{id}/move", new TurnRequest(0, "21-17"))).EnsureSuccessStatusCode();
        var stale = await client.PostAsJsonAsync($"/api/games/{id}/move", new TurnRequest(0, "21-18"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var advanced = await client.PostAsJsonAsync($"/api/games/{id}/advance", new TurnRequest(1)); advanced.EnsureSuccessStatusCode();
        var game = await advanced.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, game.GetProperty("position").GetProperty("ply").GetInt32());
        var undone = await client.PostAsJsonAsync($"/api/games/{id}/undo", new TurnRequest(2)); undone.EnsureSuccessStatusCode();
        Assert.Equal(0, (await undone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("position").GetProperty("ply").GetInt32());
    }

    [Fact] public async Task OnlineSeatsCannotBeStolenAndSpectatorsCannotMove()
    {
        using var factory = new GameFactory(); using var owner = factory.CreateClient(); using var friend = factory.CreateClient(); using var spectator = factory.CreateClient();
        var (id, token, game) = await Create(owner, "human", "human"); Authenticate(owner, token);
        Assert.Equal("waiting", game.GetProperty("status").GetString());
        var join = await friend.PostAsJsonAsync($"/api/games/{id}/join", new { }); join.EnsureSuccessStatusCode();
        var joined = await join.Content.ReadFromJsonAsync<JsonElement>(); Authenticate(friend, joined.GetProperty("token").GetString()!);
        Assert.True(joined.GetProperty("game").GetProperty("permissions").GetProperty("black").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await spectator.PostAsJsonAsync($"/api/games/{id}/join", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await spectator.PostAsJsonAsync($"/api/games/{id}/move", new TurnRequest(0, "21-17"))).StatusCode);
        (await owner.PostAsJsonAsync($"/api/games/{id}/move", new TurnRequest(0, "21-17"))).EnsureSuccessStatusCode();
        var view = await friend.GetFromJsonAsync<JsonElement>($"/api/games/{id}");
        var move = view.GetProperty("legalMoves")[0].GetProperty("id").GetString();
        (await friend.PostAsJsonAsync($"/api/games/{id}/move", new TurnRequest(1, move))).EnsureSuccessStatusCode();
        var publicJson = await spectator.GetStringAsync($"/api/games/{id}");
        Assert.DoesNotContain("Hash", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, publicJson);
    }

    [Fact] public async Task ConcurrentModelRequestsApplyOnlyOneMove()
    {
        using var factory = new GameFactory(); using var client = factory.CreateClient();
        var (id, token, _) = await Create(client, "opale", "opale"); Authenticate(client, token);
        var responses = await Task.WhenAll(client.PostAsJsonAsync($"/api/games/{id}/advance", new TurnRequest(0)), client.PostAsJsonAsync($"/api/games/{id}/advance", new TurnRequest(0)));
        Assert.Single(responses, r => r.IsSuccessStatusCode); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("illegal")][InlineData("version")][InlineData("json")][InlineData("large")][InlineData("timeout")]
    public async Task InvalidModelResponseNeverChangesPosition(string mode)
    {
        using var factory = new GameFactory(); factory.Stub.Mode = mode; using var client = factory.CreateClient();
        var (id, token, _) = await Create(client, "external-test", "human"); Authenticate(client, token);
        var answer = await client.PostAsJsonAsync($"/api/games/{id}/advance", new TurnRequest(0));
        Assert.Equal(HttpStatusCode.BadGateway, answer.StatusCode);
        var game = await client.GetFromJsonAsync<JsonElement>($"/api/games/{id}");
        Assert.Equal(0, game.GetProperty("position").GetProperty("ply").GetInt32()); Assert.Empty(game.GetProperty("history").EnumerateArray());
    }

    [Fact] public async Task HttpContractCarriesFixedCoordinatesAndLegalMoves()
    {
        using var factory = new GameFactory(); using var client = factory.CreateClient();
        var (id, token, _) = await Create(client, "external-test", "human"); Authenticate(client, token);
        (await client.PostAsJsonAsync($"/api/games/{id}/advance", new TurnRequest(0))).EnsureSuccessStatusCode();
        var request = factory.Stub.LastRequest!;
        Assert.Equal("dama-model/v1", request.ProtocolVersion); Assert.Equal("white", request.Player); Assert.Equal(7, request.LegalMoves.Length);
        Assert.Equal(-1, request.Position.Board[0]); Assert.Equal(1, request.Position.Board[31]);
        Assert.Equal("21-17", request.LegalMoves[0].Id); Assert.Equal([21, 17], request.LegalMoves[0].Path);
        Assert.Equal(100, request.ThinkTimeMs);
        var roster = await client.GetStringAsync("/api/models"); Assert.DoesNotContain("model.test", roster);
    }

    [Fact] public async Task SavedGameReloadsAndResignationIsFinal()
    {
        using var factory = new GameFactory(); using var client = factory.CreateClient();
        var (id, token, _) = await Create(client); Authenticate(client, token);
        (await client.PostAsJsonAsync($"/api/games/{id}/move", new TurnRequest(0, "21-17"))).EnsureSuccessStatusCode();
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var logger = factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameStore>>();
        var reopened = await new GameStore(config, env, logger, factory.Services.GetRequiredService<CompetitionStore>(), factory.Services.GetRequiredService<ModelRegistry>()).Find(id);
        Assert.NotNull(reopened); Assert.Equal(1, reopened.Position.Ply); Assert.True(reopened.IsOwner(token));
        var resign = await client.PostAsJsonAsync($"/api/games/{id}/resign", new TurnRequest(1)); resign.EnsureSuccessStatusCode();
        var game = await resign.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("black", game.GetProperty("result").GetProperty("winner").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/games/{id}/advance", new TurnRequest(1))).StatusCode);
    }
}
