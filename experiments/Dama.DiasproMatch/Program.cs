using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
if (args is ["--audit", var directory]) { Audit.Run(directory); return; }
var kingsrow = new Player("kingsrow", "Kingsrow Italian 1.19e", "1.19e",
    "91fbbd16ce36c2997493318b3ee9872bd564930d3fd6c2586d1bb82442e05de3", "http://127.0.0.1:9026/choose-move");
var diaspro = new Player("diaspro", "Diaspro", ManualVersion(), Hash($"models/Diaspro/Models/{ManualVersion()}.json"), "http://127.0.0.1:9017/choose-move");

async Task<Move> Ask(Player player, Position position, List<Move> legal, string gameId, List<object> history, int repetitions)
{
    var side = position.Turn == 1 ? "white" : "black";
    var request = new
    {
        protocolVersion = "dama-model/v1", rules = "italian-draughts-8x8", requestId = Guid.NewGuid().ToString("N"), gameId, player = side,
        position = new { board = position.Board, turn = side, ply = position.Ply, quietKingPlyCount = position.QuietKingPlyCount, repetitionCount = repetitions },
        legalMoves = legal.Select(m => new { id = m.Id, from = m.From + 1, to = m.To + 1, path = m.Path.Select(s => s + 1), captures = m.Captures.Select(s => s + 1), promotes = m.Promotes }),
        recentMoves = history.TakeLast(16), thinkTimeMs = 1100
    };
    using var response = await http.PostAsJsonAsync(player.Endpoint, request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{player.Id}: HTTP {(int)response.StatusCode}: {text}");
    var answer = JsonSerializer.Deserialize<Answer>(text, json);
    return answer?.ProtocolVersion == "dama-model/v1" && legal.Find(m => m.Id == answer.MoveId) is { } found
        ? found : throw new InvalidOperationException($"Risposta illegale di {player.Id}: {text}");
}

if (args.Contains("--check"))
{
    var fixtures = new List<Position> { Position.Initial(), Position.Initial() with { Turn = -1 } };
    foreach (var pieces in new[] { new[] { (21, 1), (17, -1), (9, -1) }, new[] { (9, 2), (13, -1), (22, -2) }, new[] { (5, 1), (28, -1) } })
    {
        var board = new int[32];
        foreach (var (square, piece) in pieces) board[square - 1] = piece;
        fixtures.Add(new Position(board));
        fixtures.Add(new Position(board.Reverse().Select(piece => -piece).ToArray(), -1));
    }
    foreach (var position in fixtures)
    {
        var legal = Rules.LegalMoves(position);
        var selected = await Ask(diaspro, position, legal, Guid.NewGuid().ToString("N"), [], 1);
        Console.WriteLine($"Diaspro fixture turn={position.Turn}, legal={legal.Count}: {selected.Id}");
    }
    var kingsrowMove = await Ask(kingsrow, Position.Initial(), Rules.LegalMoves(Position.Initial()), Guid.NewGuid().ToString("N"), [], 1);
    Console.WriteLine($"Kingsrow fixture: {kingsrowMove.Id}");
    return;
}

if (args.Length < 1) throw new ArgumentException("Uso: directory [--games N pari] [--seed N] [--source test|benchmark], --check, --audit directory.");
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new InvalidOperationException("La directory risultati deve essere nuova.");
var games = IntOption("--games", 18);
var seed = IntOption("--seed", 31001);
var source = StringOption("--source", "test");
var buildConfiguration = StringOption("--configuration", "Debug");
if (games < 2 || games % 2 != 0) throw new ArgumentException("Il numero di partite deve essere pari e positivo.");
Directory.CreateDirectory(output);
var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
const int limit = 400;
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
{
    startedAt = DateTimeOffset.UtcNow, seed, games, openingPlies = 6, plyLimit = limit, thinkTimeMs = 1100,
    effectiveSearchMs = 1000, threads = 1, memoryLimitMiB = 1024, ponder = false, source, kingsrow, diaspro,
    competitionDirectory = store.DirectoryPath, rules = "italian-draughts-8x8", training = false,
    endgameDatabases = false, kingsrowOpeningBook = "bundled; engine defaults", diasproSearch = "bitboard-pvs-tt-native-leaves-move-hints",
    diasproArchitecture = "relational graph with episode-end self-play outcome strategic and symmetric square/near-pattern heads, native-bitboard leaf evaluation, PVS/TT and per-game move-ordering memory", logicalProcessor = 2,
    buildConfiguration, diasproSources = SourceHashes()
}, new JsonSerializerOptions(json) { WriteIndented = true }));
var results = new List<Result>();
using var stream = new StreamWriter(Path.Combine(output, "games.jsonl"));
for (var n = 0; n < games; n++)
{
    using var matchLock = AcquireMatchLock();
    var white = n % 2 == 0 ? kingsrow : diaspro;
    var black = n % 2 == 0 ? diaspro : kingsrow;
    using var tracked = store.Start(source, Participant.Ai(white.Id, white.Name), Participant.Ai(black.Id, black.Name));
    var position = Position.Initial();
    var seen = new Dictionary<string, int> { [position.Key] = 1 };
    var history = new List<object>();
    var frames = new List<object> { new { ply = 0, board = position.Board, turn = position.Turn } };
    var random = new Random(seed + n / 2);
    Outcome? outcome = null;
    string? error = null;
    try
    {
        while (true)
        {
            var legal = Rules.LegalMoves(position);
            outcome = Rules.GetOutcome(position, seen.GetValueOrDefault(position.Key), legal);
            if (outcome is not null || position.Ply >= limit) break;
            var player = position.Turn == 1 ? white : black;
            var started = System.Diagnostics.Stopwatch.StartNew();
            var move = position.Ply < 6 ? legal[random.Next(legal.Count)] : await Ask(player, position, legal, tracked.Id, history, seen.GetValueOrDefault(position.Key));
            var elapsedMs = started.Elapsed.TotalMilliseconds;
            history.Add(new { ply = position.Ply + 1, player = position.Turn == 1 ? "white" : "black", moveId = move.Id });
            position = Rules.Apply(position, move);
            seen[position.Key] = seen.GetValueOrDefault(position.Key) + 1;
            frames.Add(new { ply = position.Ply, board = position.Board, turn = position.Turn, moveId = move.Id, elapsedMs, opening = position.Ply <= 6 });
        }
    }
    catch (Exception ex) { error = ex.Message; }
    if (outcome is not null) tracked.Complete(outcome.Winner, position.Ply, outcome.Reason);
    else tracked.Interrupt(position.Ply, error is null ? "ply-limit" : "model-failure");
    var result = new Result(tracked.Id, n + 1, seed + n / 2, white.Id, black.Id, outcome is not null, outcome?.Winner,
        outcome?.Reason ?? (error is null ? "ply-limit" : "model-failure"), position.Ply, error, frames);
    results.Add(result);
    stream.WriteLine(JsonSerializer.Serialize(result, json));
    await stream.FlushAsync();
    File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
    {
        played = results.Count,
        diasproWins = results.Count(r => r.Completed && r.Winner != 0 && (r.Winner == 1 ? r.White : r.Black) == "diaspro"),
        draws = results.Count(r => r.Completed && r.Winner == 0),
        kingsrowWins = results.Count(r => r.Completed && r.Winner != 0 && (r.Winner == 1 ? r.White : r.Black) == "kingsrow"),
        interrupted = results.Count(r => !r.Completed)
    }, new JsonSerializerOptions(json) { WriteIndented = true }));
    Console.WriteLine($"{results.Count}/{games} {white.Id}-{black.Id}: {(outcome is null ? "interrotta" : outcome.Winner == 0 ? "patta" : outcome.Winner == 1 ? white.Id : black.Id)} ({position.Ply} ply) {error}");
    if (error is not null) throw new InvalidOperationException("Misura fermata per errore del modello; traiettoria conservata. " + error);
}

int IntOption(string name, int fallback) { var index = Array.IndexOf(args, name); return index >= 0 ? int.Parse(args[index + 1]) : fallback; }
string StringOption(string name, string fallback) { var index = Array.IndexOf(args, name); return index >= 0 ? args[index + 1] : fallback; }
static string ManualVersion() => Diaspro.ManualWeights.Version;
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static Dictionary<string, string> SourceHashes() => new[]
{
    "models/Diaspro/ManualWeights.cs", "models/Diaspro/LearnedPatterns.cs", "models/Diaspro/Network.cs", "models/Diaspro/FastRules.cs",
    "models/Diaspro/Bitboard.cs", "models/Diaspro/DeepSearch.cs", "models/Diaspro/Endgame.cs", "models/Diaspro/Search.cs",
    "models/Diaspro/Policy.cs", "models/Diaspro/PositionMemory.cs",
    "models/Diaspro/Program.cs", "experiments/Dama.DiasproMatch/Program.cs", "experiments/Dama.DiasproMatch/Audit.cs",
    "experiments/Dama.DiasproLearn/Program.cs", "experiments/Dama.DiasproLearn/LearningAudit.cs"
}.ToDictionary(path => path, Hash);
static FileStream AcquireMatchLock()
{
    var path = "arena/kingsrow-challenge/matches-9026.lock";
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    while (true)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { Thread.Sleep(100); }
    }
}

record Player(string Id, string Name, string Version, string Sha256, string Endpoint);
record Answer(string ProtocolVersion, string MoveId);
record Result(string MatchId, int Number, int OpeningSeed, string White, string Black, bool Completed, int? Winner,
    string Reason, int Plies, string? Error, List<object> Frames);
