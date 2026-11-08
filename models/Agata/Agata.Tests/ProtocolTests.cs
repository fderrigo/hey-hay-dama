using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dama.Engine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agata.Tests;

/// <summary>Conformance to dama-model/v1, following section 8 of the specification.</summary>
public class ProtocolTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/choose-move";

    private static object For(Position p, int think = 300, string? game = null)
    {
        var player = p.Turn == 1 ? "white" : "black";
        return new
        {
            protocolVersion = "dama-model/v1", rules = "italian-draughts-8x8", requestId = Guid.NewGuid().ToString("N"),
            gameId = game ?? Guid.NewGuid().ToString("N"), player,
            position = new { board = p.Board, turn = player, ply = p.Ply, quietKingPlyCount = p.QuietKingPlyCount, repetitionCount = 1 },
            legalMoves = Rules.LegalMoves(p).Select(m => new
            {
                id = m.Id, from = m.From + 1, to = m.To + 1, path = m.Path.Select(x => x + 1), captures = m.Captures.Select(x => x + 1), promotes = m.Promotes
            }),
            recentMoves = Array.Empty<object>(), thinkTimeMs = think
        };
    }

    private static int[] Board(params (string Square, int Piece)[] pieces)
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[Rules.ParseSquare(square)] = piece;
        return board;
    }

    private static async Task<string> MoveId(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length <= 16 * 1024);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("dama-model/v1", json.RootElement.GetProperty("protocolVersion").GetString());
        return json.RootElement.GetProperty("moveId").GetString()!;
    }

    [Fact]
    public async Task Opening_from_the_specification_returns_one_of_the_seven_ids()
    {
        const string body = """
        {"protocolVersion":"dama-model/v1","rules":"italian-draughts-8x8","requestId":"e24df8e24fd64a08b845b5412793941d","gameId":"c55c39e9120c4d99b174f4aad0cdf2a5","player":"white",
         "position":{"board":[-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1],"turn":"white","ply":0,"quietKingPlyCount":0,"repetitionCount":1},
         "legalMoves":[{"id":"21-17","from":21,"to":17,"path":[21,17],"captures":[],"promotes":false},{"id":"21-18","from":21,"to":18,"path":[21,18],"captures":[],"promotes":false},
          {"id":"22-18","from":22,"to":18,"path":[22,18],"captures":[],"promotes":false},{"id":"22-19","from":22,"to":19,"path":[22,19],"captures":[],"promotes":false},
          {"id":"23-19","from":23,"to":19,"path":[23,19],"captures":[],"promotes":false},{"id":"23-20","from":23,"to":20,"path":[23,20],"captures":[],"promotes":false},
          {"id":"24-20","from":24,"to":20,"path":[24,20],"captures":[],"promotes":false}],
         "recentMoves":[],"thinkTimeMs":5000,"unknownField":true}
        """;
        var id = await MoveId(await factory.CreateClient().PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json")));
        Assert.Contains(id, new[] { "21-17", "21-18", "22-18", "22-19", "23-19", "23-20", "24-20" });
    }

    [Fact]
    public async Task Black_to_move_keeps_the_absolute_meaning_of_board_signs()
    {
        var p = Position.Initial();
        p = Rules.Apply(p, Rules.LegalMoves(p)[0]);
        Assert.Contains(await MoveId(await factory.CreateClient().PostAsJsonAsync(Url, For(p))), Rules.LegalMoves(p).Select(m => m.Id));
    }

    [Fact]
    public async Task Multiple_capture_returns_the_id_of_the_full_path()
    {
        var p = new Position(Board(("A2", 1), ("B3", -1), ("D5", -1), ("H1", 1)));
        var legal = Rules.LegalMoves(p);
        Assert.Contains(legal, m => m.Captures.Length == 2);
        Assert.Equal(legal.Single().Id, await MoveId(await factory.CreateClient().PostAsJsonAsync(Url, For(p))));
    }

    [Fact]
    public async Task Two_paths_with_the_same_ends_are_told_apart_by_id()
    {
        var p = new Position(Board(("D3", 2), ("C4", -1), ("E4", -1), ("C6", -1), ("E6", -1)));
        var legal = Rules.LegalMoves(p);
        Assert.Contains(legal.GroupBy(m => (m.From, m.To)), g => g.Count() > 1);
        Assert.Contains(await MoveId(await factory.CreateClient().PostAsJsonAsync(Url, For(p))), legal.Select(m => m.Id));
    }

    [Fact]
    public async Task Promotion_is_a_legal_answer()
    {
        var p = new Position(Board(("B7", 1), ("H1", -1)));
        var legal = Rules.LegalMoves(p);
        Assert.Contains(legal, m => m.Promotes);
        Assert.Contains(await MoveId(await factory.CreateClient().PostAsJsonAsync(Url, For(p))), legal.Select(m => m.Id));
    }

    [Fact]
    public async Task Concurrent_games_each_receive_a_move_legal_for_their_own_position()
    {
        var client = factory.CreateClient();
        var random = new Random(9);
        var positions = Enumerable.Range(0, 64).Select(_ =>
        {
            var p = Position.Initial();
            for (var i = random.Next(1, 24); i > 0; i--)
            {
                var next = Rules.Apply(p, Rules.LegalMoves(p)[random.Next(Rules.LegalMoves(p).Count)]);
                if (Rules.LegalMoves(next).Count == 0) break;
                p = next;
            }
            return p;
        }).ToArray();
        var answers = await Task.WhenAll(positions.Select(async p => (p, id: await MoveId(await client.PostAsJsonAsync(Url, For(p))))));
        foreach (var (p, id) in answers) Assert.Contains(id, Rules.LegalMoves(p).Select(m => m.Id));
    }

    [Fact]
    public async Task Answers_well_inside_the_shortest_allowed_time()
    {
        var client = factory.CreateClient();
        await MoveId(await client.PostAsJsonAsync(Url, For(Position.Initial())));
        var watch = Stopwatch.StartNew();
        await MoveId(await client.PostAsJsonAsync(Url, For(Position.Initial(), 100)));
        Assert.True(watch.ElapsedMilliseconds < 100, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Fixed_seed_repeats_the_choice_for_the_same_game_and_ply()
    {
        using var seeded = factory.WithWebHostBuilder(b => b.UseSetting("Agata:Seed", "42"));
        var client = seeded.CreateClient();
        var p = Position.Initial();
        var ids = new List<string>();
        for (var i = 0; i < 5; i++) ids.Add(await MoveId(await client.PostAsJsonAsync(Url, For(p, game: "partita-fissa"))));
        Assert.Single(ids.Distinct());
    }

    [Theory]
    [InlineData("""{"protocolVersion":"dama-model/v2"}""")]
    [InlineData("""{"protocolVersion":"dama-model/v1","rules":"altro"}""")]
    [InlineData("""{"protocolVersion":"dama-model/v1","rules":"italian-draughts-8x8","player":"white","position":{"board":[1],"turn":"white"},"legalMoves":[],"thinkTimeMs":100}""")]
    [InlineData("""{"protocolVersion":"dama-model/v1","rules":"italian-draughts-8x8","player":"white","position":{"board":[-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1],"turn":"black"},"legalMoves":[],"thinkTimeMs":100}""")]
    [InlineData("""{"protocolVersion":"dama-model/v1","rules":"italian-draughts-8x8","player":"white","position":{"board":[-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1],"turn":"white"},"legalMoves":[],"thinkTimeMs":100}""")]
    [InlineData("non json")]
    [InlineData("null")]
    public async Task Invalid_requests_receive_400(string body)
    {
        var response = await factory.CreateClient().PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_content_type_receives_415()
    {
        var response = await factory.CreateClient().PostAsync(Url, new StringContent("{}", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Token_is_enforced_when_configured()
    {
        const string variable = "AGATA_TEST_TOKEN";
        Environment.SetEnvironmentVariable(variable, "segreto-di-prova");
        using var secured = factory.WithWebHostBuilder(b => b.UseSetting("Agata:ApiKeyEnvironmentVariable", variable));
        var client = secured.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Url, For(Position.Initial()))).StatusCode);
        using var message = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(For(Position.Initial())) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "segreto-di-prova");
        await MoveId(await client.SendAsync(message));
    }
}
