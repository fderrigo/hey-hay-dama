using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dama.Competition;

public sealed record Participant(string Id, string Name, string Kind)
{
    public static Participant Ai(string id, string name) => new("ai:" + id, name, "ai");
    public static string? Nickname(string? value)
    {
        if (value is null) return null;
        var name = Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ");
        return name.Length is >= 2 and <= 24 && Regex.IsMatch(name, @"^[\p{L}\p{N} ._-]+$") ? name : null;
    }
    public static Participant Human(string name)
    {
        name = Nickname(name) ?? throw new ArgumentException("Nickname non valido.");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()))).ToLowerInvariant();
        return new("human:" + key, name, "human");
    }
}

public sealed record MatchRecord(string Id, string Source, Participant? White, Participant? Black,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, string Status, int? Winner, string? Reason, int Plies)
{
    public string Type => White?.Kind == "ai" && Black?.Kind == "ai" ? "ai-ai"
        : White?.Kind != "ai" && Black?.Kind != "ai" ? "human-human" : "human-ai";
}

public sealed class Standing(Participant player)
{
    public string Id { get; } = player.Id;
    public string Name { get; } = player.Name;
    public string Kind { get; } = player.Kind;
    public int Rank { get; set; }
    public int Started { get; set; }
    public int Played { get; set; }
    public int InProgress { get; set; }
    public int Interrupted { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int SelfPlay { get; set; }
    public int Points => Wins * 2 + Draws;
}

public sealed record Leaderboard(string Source, string Type, int TotalStarted, int TotalFinished, int InProgress,
    int Interrupted, Standing[] Entries, MatchRecord[] RecentMatches);

/// <summary>One durable record per match, shared by the web server and offline runners.</summary>
public sealed class CompetitionStore
{
    public string DirectoryPath { get; }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public CompetitionStore(string directory) { DirectoryPath = Path.GetFullPath(directory); Directory.CreateDirectory(DirectoryPath); }

    public static string DefaultDirectory()
    {
        if (Environment.GetEnvironmentVariable("DAMA_COMPETITION_DIRECTORY") is { Length: > 0 } configured)
            return Path.GetFullPath(configured);
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "WeightSpaceGames.slnx")))
                    return Path.Combine(dir.FullName, "src", "Dama.Web", "App_Data", "competition");
        return Path.Combine(AppContext.BaseDirectory, "App_Data", "competition");
    }

    public static bool ValidSource(string value) => value is "test" or "benchmark" or "web";
    public static bool ValidType(string value) => value is "all" or "human-human" or "ai-ai" or "human-ai";

    // FileShare.None serializes independent processes as well as concurrent ASP.NET requests.
    private FileStream Acquire()
    {
        var deadline = Environment.TickCount64 + 10000;
        while (true)
        {
            try { return new FileStream(Path.Combine(DirectoryPath, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline) { Thread.Sleep(10); }
        }
    }

    public void Save(MatchRecord match)
    {
        if (!Guid.TryParseExact(match.Id, "N", out _) || !ValidSource(match.Source)
            || match.Status is not ("waiting" or "active" or "finished" or "interrupted") || match.Plies < 0
            || match.Winner is < -1 or > 1 || (match.Status == "finished") != (match.Winner is not null)
            || match.Status == "finished" && (match.White is null || match.Black is null))
            throw new ArgumentException("Registrazione della partita non valida.");
        foreach (var player in new[] { match.White, match.Black }.OfType<Participant>())
            if (string.IsNullOrWhiteSpace(player.Id) || player.Id.Length > 128 || string.IsNullOrWhiteSpace(player.Name)
                || player.Name.Length > 80 || player.Kind is not ("ai" or "human"))
                throw new ArgumentException("Partecipante non valido.");
        using var gate = Acquire();
        var path = Path.Combine(DirectoryPath, match.Id + ".json");
        if (File.Exists(path))
        {
            var previous = JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(path), Json)!;
            if (previous.Source != match.Source || previous.StartedAt != match.StartedAt)
                throw new InvalidOperationException("L'identità della partita non può cambiare.");
            if (previous.UpdatedAt > match.UpdatedAt) return;
        }
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(match, Json));
        File.Move(temporary, path, true);
    }

    public MatchRecord[] Read()
    {
        using var gate = Acquire();
        return Directory.EnumerateFiles(DirectoryPath, "*.json")
            .Select(path => JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"Partita non leggibile: {path}"))
            .OrderByDescending(m => m.StartedAt).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }

    public Leaderboard GetLeaderboard(string source = "all", string type = "all")
    {
        if (source != "all" && !ValidSource(source) || !ValidType(type)) throw new ArgumentException("Filtro non valido.");
        var matches = Read().Where(m => (source == "all" || m.Source == source) && (type == "all" || m.Type == type)).ToArray();
        var entries = new Dictionary<string, Standing>(StringComparer.Ordinal);
        foreach (var match in matches)
        {
            foreach (var player in new[] { match.White, match.Black }.OfType<Participant>().DistinctBy(p => p.Id))
            {
                if (!entries.TryGetValue(player.Id, out var standing)) entries[player.Id] = standing = new Standing(player);
                standing.Started++;
                if (match.Status is "waiting" or "active") { standing.InProgress++; continue; }
                if (match.Status == "interrupted") { standing.Interrupted++; continue; }
                standing.Played++;
                if (match.White!.Id == match.Black!.Id) { standing.SelfPlay++; continue; }
                if (match.Winner == 0) standing.Draws++;
                else if ((match.Winner == 1 ? match.White.Id : match.Black.Id) == player.Id) standing.Wins++;
                else standing.Losses++;
            }
        }
        var ordered = entries.Values.OrderByDescending(s => s.Points).ThenByDescending(s => s.Wins)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < ordered.Length; i++) ordered[i].Rank = i + 1;
        return new(source, type, matches.Length, matches.Count(m => m.Status == "finished"),
            matches.Count(m => m.Status is "waiting" or "active"), matches.Count(m => m.Status == "interrupted"), ordered, matches.Take(50).ToArray());
    }

    public TrackedMatch Start(string source, Participant white, Participant black) => new(this, source, white, black);
}

/// <summary>Offline runners register before playing; exceptions leave an interrupted match, never fabricated points.</summary>
public sealed class TrackedMatch : IDisposable
{
    private readonly CompetitionStore store;
    private MatchRecord record;
    private bool finished;
    public string Id => record.Id;
    internal TrackedMatch(CompetitionStore store, string source, Participant white, Participant black)
    {
        this.store = store;
        var now = DateTimeOffset.UtcNow;
        record = new(Guid.NewGuid().ToString("N"), source, white, black, now, now, "active", null, null, 0);
        store.Save(record);
    }
    public void Complete(int winner, int plies, string reason)
    {
        if (finished) throw new InvalidOperationException("Partita già conclusa.");
        record = record with { Status = "finished", Winner = winner, Plies = plies, Reason = reason, UpdatedAt = DateTimeOffset.UtcNow };
        store.Save(record); finished = true;
    }
    public void Dispose()
    {
        if (!finished) store.Save(record with { Status = "interrupted", Winner = null, Reason = "runner-interrupted", UpdatedAt = DateTimeOffset.UtcNow });
    }
    public void Interrupt(int plies, string reason)
    {
        if (finished) throw new InvalidOperationException("Partita già conclusa.");
        record = record with { Status = "interrupted", Winner = null, Plies = plies, Reason = reason, UpdatedAt = DateTimeOffset.UtcNow };
        store.Save(record); finished = true;
    }
}
