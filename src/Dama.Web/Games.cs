using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dama.Engine;
using Dama.Competition;

namespace Dama.Web;

public sealed record MoveRecord(int Ply, string Player, string MoveId, string Notation, int[] Path, int[] Captures, bool Promotes, int ThinkingMs, string? Comment);
public sealed record CreateGame(string White, string Black, string Access = "online", string HumanSide = "white",
    string? WhiteNickname = null, string? BlackNickname = null, string? Source = null);
public sealed record JoinGame(string? Nickname = null);
public sealed record TurnRequest(int ExpectedPly, string? MoveId = null);

public sealed class GameRoom
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string White { get; set; } = "human";
    public string Black { get; set; } = "opale";
    public string Access { get; set; } = "online";
    public string Source { get; set; } = "web";
    public string? WhiteNickname { get; set; }
    public string? BlackNickname { get; set; }
    public string OwnerHash { get; set; } = "";
    public string? WhiteSeatHash { get; set; }
    public string? BlackSeatHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Position Position { get; set; } = Position.Initial();
    public List<MoveRecord> History { get; set; } = [];
    public Dictionary<string, int> Repetitions { get; set; } = new() { [Position.Initial().Key] = 1 };
    public Outcome? Outcome { get; set; }
    [JsonIgnore] public SemaphoreSlim Gate { get; } = new(1, 1);
    [JsonIgnore] public string CurrentPlayer => Position.Turn == 1 ? White : Black;
    [JsonIgnore] public bool Waiting => Access == "online" && (White == "human" && WhiteSeatHash is null || Black == "human" && BlackSeatHash is null);

    public bool IsOwner(string? token) => Matches(token, OwnerHash);
    public bool Controls(int side, string? token) => Access == "local" && IsOwner(token) || Matches(token, side == 1 ? WhiteSeatHash : BlackSeatHash);
    public bool CanAdvance(string? token) => IsOwner(token) || Controls(1, token) || Controls(-1, token);
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool Matches(string? token, string? hash) => token is { Length: 64 } && hash is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(token)), Encoding.UTF8.GetBytes(hash));

    public void Play(Move move, int elapsed = 0, string? comment = null)
    {
        var side = Position.Turn == 1 ? "white" : "black";
        Position = Rules.Apply(Position, move);
        History.Add(new MoveRecord(Position.Ply, side, move.Id, move.Notation, move.Path.Select(i => i + 1).ToArray(), move.Captures.Select(i => i + 1).ToArray(), move.Promotes, elapsed, comment));
        Repetitions.TryGetValue(Position.Key, out var repetitions);
        Repetitions[Position.Key] = ++repetitions;
        Outcome = Rules.GetOutcome(Position, repetitions);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public object View(ModelRegistry registry, string? token)
    {
        var active = Outcome is null && !Waiting;
        var legal = active ? Rules.LegalMoves(Position) : [];
        return new
        {
            Id, Access, Source, CreatedAt, UpdatedAt,
            Position = new PositionView(Position.Board, Position.Turn == 1 ? "white" : "black", Position.Ply, Position.QuietKingPlyCount, Repetitions.GetValueOrDefault(Position.Key, 1)),
            White = new { Id = ModelRegistry.CanonicalId(White), Name = White == "human" ? WhiteNickname ?? "Posto libero" : registry.Name(White), IsHuman = White == "human", Connected = White != "human" || Access == "local" || WhiteSeatHash is not null },
            Black = new { Id = ModelRegistry.CanonicalId(Black), Name = Black == "human" ? BlackNickname ?? "Posto libero" : registry.Name(Black), IsHuman = Black == "human", Connected = Black != "human" || Access == "local" || BlackSeatHash is not null },
            Status = Outcome is not null ? "finished" : Waiting ? "waiting" : "active",
            Result = Outcome is null ? null : new { Winner = Outcome.Winner == 0 ? "draw" : Outcome.Winner == 1 ? "white" : "black", Outcome.Reason },
            LegalMoves = legal.Select(MoveView.FromMove).ToArray(), History = History.ToArray(),
            Permissions = new
            {
                IsOwner = IsOwner(token), White = Controls(1, token), Black = Controls(-1, token),
                CanMove = active && CurrentPlayer == "human" && Controls(Position.Turn, token),
                CanAdvance = active && CurrentPlayer != "human" && CanAdvance(token),
                CanJoin = Waiting && !Controls(1, token) && !Controls(-1, token) && !IsOwner(token),
                CanUndo = IsOwner(token) && Outcome?.Reason != "resigned" && History.Count > 0 && (White == "human") != (Black == "human")
            }
        };
    }
}

public sealed class GameStore
{
    private readonly ConcurrentDictionary<string, GameRoom> rooms = new();
    private readonly string directory;
    private readonly ILogger<GameStore> logger;
    private readonly CompetitionStore competition;
    private readonly ModelRegistry models;
    private readonly SemaphoreSlim creationGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public GameStore(IConfiguration configuration, IWebHostEnvironment environment, ILogger<GameStore> logger, CompetitionStore competition, ModelRegistry models)
    {
        this.logger = logger;
        this.competition = competition;
        this.models = models;
        directory = Path.GetFullPath(configuration["Arena:DataDirectory"] ?? "App_Data/games", environment.ContentRootPath);
        Directory.CreateDirectory(directory);
    }

    public async Task<GameRoom?> Find(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        if (rooms.TryGetValue(id, out var room)) return room;
        await creationGate.WaitAsync();
        try
        {
            if (rooms.TryGetValue(id, out room)) return room;
            var path = Path.Combine(directory, id + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                room = JsonSerializer.Deserialize<GameRoom>(await File.ReadAllTextAsync(path), Json);
                if (room is not null) rooms[id] = room;
                return room;
            }
            catch (JsonException ex) { logger.LogError(ex, "Cannot load game {GameId}", id); return null; }
        }
        finally { creationGate.Release(); }
    }

    public async Task Add(GameRoom room) { await Save(room); rooms[room.Id] = room; }
    public async Task Save(GameRoom room)
    {
        var path = Path.Combine(directory, room.Id + ".json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(room, Json));
        File.Move(temp, path, true);
        Register(room);
    }

    private void Register(GameRoom room)
    {
        Participant? Player(string id, string? nickname) => id == "human"
            ? nickname is null ? null : Participant.Human(nickname)
            : Participant.Ai(ModelRegistry.CanonicalId(id), models.Name(id));
        competition.Save(new MatchRecord(room.Id, room.Source, Player(room.White, room.WhiteNickname), Player(room.Black, room.BlackNickname),
            room.CreatedAt, room.UpdatedAt, room.Outcome is not null ? "finished" : room.Waiting ? "waiting" : "active",
            room.Outcome?.Winner, room.Outcome?.Reason, room.Position.Ply));
    }

    public void Reconcile()
    {
        // Recover a match saved immediately before a process stopped while updating the standings.
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            if (JsonSerializer.Deserialize<GameRoom>(File.ReadAllText(path), Json) is { } room) Register(room);
    }
}
