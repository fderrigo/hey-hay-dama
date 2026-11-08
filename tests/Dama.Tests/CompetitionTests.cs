using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dama.Competition;
using Dama.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Dama.Tests;

public sealed class CompetitionTests : IDisposable
{
    // Synthetic arithmetic fixtures are not played matches; they use an isolated ledger.
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dama-competition-fixtures", Guid.NewGuid().ToString("N"));
    private readonly Participant human = Participant.Human("Alice");
    private readonly Participant otherHuman = Participant.Human("Bruno");
    private readonly Participant ai = Participant.Ai("fixture-a", "Fixture A");
    private readonly Participant otherAi = Participant.Ai("fixture-b", "Fixture B");
    private MatchRecord Finished(string source, Participant white, Participant black, int winner)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid().ToString("N"), source, white, black, now, now, "finished", winner, "fixture", 4);
    }

    [Fact]
    public void GeneralAndCombinedFiltersUseTheSameResults()
    {
        var store = new CompetitionStore(directory);
        store.Save(Finished("web", human, otherHuman, 1));
        store.Save(Finished("web", human, ai, 0));
        store.Save(Finished("test", human, ai, -1));
        store.Save(Finished("benchmark", ai, otherAi, 1));
        var all = store.GetLeaderboard();
        Assert.Equal(4, all.TotalFinished);
        Assert.Equal(3, all.Entries.Single(e => e.Id == ai.Id).Started);
        Assert.Equal(5, all.Entries.Single(e => e.Id == ai.Id).Points);
        Assert.Equal(3, all.Entries.Single(e => e.Id == human.Id).Points);
        var webHumanAi = store.GetLeaderboard("web", "human-ai");
        Assert.Single(webHumanAi.RecentMatches);
        Assert.All(webHumanAi.Entries, e => Assert.Equal(1, e.Points));
        Assert.Single(store.GetLeaderboard("all", "human-human").RecentMatches);
        Assert.Single(store.GetLeaderboard("benchmark", "ai-ai").RecentMatches);
        Assert.Empty(store.GetLeaderboard("benchmark", "human-human").Entries);
    }

    [Fact]
    public void RepeatedSavesAndRestartNeverDuplicateAMatchAndUndoRemovesPoints()
    {
        var store = new CompetitionStore(directory);
        var match = Finished("web", human, ai, 1);
        store.Save(match); store.Save(match);
        Assert.Equal(1, new CompetitionStore(directory).GetLeaderboard().TotalFinished);
        var active = match with { UpdatedAt = match.UpdatedAt.AddSeconds(1), Status = "active", Winner = null, Reason = null, Plies = 2 };
        store.Save(active);
        store.Save(match); // A stale write must not restore a result after an undo.
        var result = store.GetLeaderboard();
        Assert.Equal(1, result.TotalStarted); Assert.Equal(1, result.InProgress); Assert.Equal(0, result.TotalFinished);
        Assert.All(result.Entries, e => Assert.Equal(0, e.Points));
    }

    [Fact]
    public void SelfPlayAndInterruptedRunnersAreCountedWithoutPoints()
    {
        var store = new CompetitionStore(directory);
        store.Save(Finished("test", ai, ai, 1));
        using (store.Start("benchmark", ai, otherAi)) { }
        var result = store.GetLeaderboard();
        Assert.Equal(2, result.TotalStarted); Assert.Equal(1, result.Interrupted);
        var row = result.Entries.Single(e => e.Id == ai.Id);
        Assert.Equal(2, row.Started); Assert.Equal(1, row.SelfPlay); Assert.Equal(0, row.Points);
    }

    [Fact]
    public async Task IndependentStoreInstancesCanWriteConcurrently()
    {
        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => new CompetitionStore(directory).Save(Finished("test", ai, otherAi, 0)))));
        var result = new CompetitionStore(directory).GetLeaderboard();
        Assert.Equal(24, result.TotalFinished); Assert.All(result.Entries, e => Assert.Equal(24, e.Points));
    }

    [Fact]
    public void NicknamesAreCanonicalAndInvalidRecordsAreRejected()
    {
        Assert.Equal(Participant.Human("Alice").Id, Participant.Human(" alice ").Id);
        Assert.Equal("Ada Lovelace", Participant.Nickname(" Ada   Lovelace "));
        Assert.Null(Participant.Nickname("<script>"));
        var store = new CompetitionStore(directory);
        Assert.Throws<ArgumentException>(() => store.Save(Finished("unknown", human, ai, 1)));
        Assert.Throws<ArgumentException>(() => store.Save(Finished("test", human, ai, 2)));
        Assert.Throws<ArgumentException>(() => store.GetLeaderboard("test", "invalid"));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

public sealed class CompetitionApiTests
{
    [Fact]
    public async Task HumanMatchIsRegisteredFromCreationThroughResignationExactlyOnce()
    {
        using var factory = new GameFactory(); using var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await client.PostAsJsonAsync("/api/games", new CreateGame("human", "human", "local", WhiteNickname: "Test Bianco " + suffix, BlackNickname: "Test Nero " + suffix));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("game").GetProperty("id").GetString()!;
        Assert.Equal("test", created.GetProperty("game").GetProperty("source").GetString());
        client.DefaultRequestHeaders.Add("X-Game-Token", created.GetProperty("token").GetString());
        var store = factory.Services.GetRequiredService<CompetitionStore>();
        Assert.Equal("active", store.Read().Single(m => m.Id == id).Status);
        (await client.PostAsJsonAsync($"/api/games/{id}/resign", new TurnRequest(0))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/games/{id}/resign", new TurnRequest(0))).StatusCode);
        var standings = await client.GetFromJsonAsync<JsonElement>("/api/leaderboard?source=test&type=human-human");
        var row = standings.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "Test Nero " + suffix);
        Assert.Equal(2, row.GetProperty("points").GetInt32()); Assert.Equal(1, row.GetProperty("played").GetInt32());
        Assert.Single(store.Read(), m => m.Id == id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/leaderboard?type=wrong")).StatusCode);
    }

    [Fact]
    public async Task OnlineNicknameIsAttachedOnlyWhenTheSeatIsClaimed()
    {
        using var factory = new GameFactory(); using var owner = factory.CreateClient(); using var guest = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var name = "Test Amico " + suffix;
        var response = await owner.PostAsJsonAsync("/api/games", new CreateGame("human", "human", WhiteNickname: name));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("game").GetProperty("id").GetString()!;
        var store = factory.Services.GetRequiredService<CompetitionStore>();
        Assert.Null(store.Read().Single(m => m.Id == id).Black);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync($"/api/games/{id}/join", new JoinGame(name.ToUpperInvariant()))).StatusCode);
        var joined = await guest.PostAsJsonAsync($"/api/games/{id}/join", new JoinGame("Test Ospite " + suffix));
        joined.EnsureSuccessStatusCode();
        var game = await joined.Content.ReadFromJsonAsync<JsonElement>();
        guest.DefaultRequestHeaders.Add("X-Game-Token", game.GetProperty("token").GetString());
        (await guest.PostAsJsonAsync($"/api/games/{id}/resign", new TurnRequest(0))).EnsureSuccessStatusCode();
        var match = store.Read().Single(m => m.Id == id);
        Assert.Equal("human-human", match.Type); Assert.Equal("Test Ospite " + suffix, match.Black!.Name); Assert.Equal(1, match.Winner);
    }
}
