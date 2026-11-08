using System.Net.Http.Json;
using System.Text.Json;
using Dama.Engine;
using Dama.Competition;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "WeightSpaceGames.slnx"))) throw new InvalidOperationException("Avviare dalla radice del repository.");
if (args is ["--audit", var directory]) { Audit.Run(directory); return; }
var kingsrow = new Player("kingsrow", "Kingsrow Italian 1.19e", "1.19e", "91fbbd16ce36c2997493318b3ee9872bd564930d3fd6c2586d1bb82442e05de3", "http://127.0.0.1:9015/choose-move");
var opponents = Directory.GetFiles("arena/coordinamento/iscrizioni", "AGENTE-*.json").Order()
    .Select(p => JsonSerializer.Deserialize<Player>(File.ReadAllText(p), json)!).ToArray();
if (opponents.Length != 3) throw new InvalidOperationException("Sono richiesti i tre motori iscritti.");

async Task<Move> Ask(Player player, Position p, List<Move> legal, string id, List<object> history, int repetitions)
{
    var side = p.Turn == 1 ? "white" : "black";
    var request = new {
        protocolVersion = "dama-model/v1", rules = "italian-draughts-8x8", requestId = Guid.NewGuid().ToString("N"), gameId = id, player = side,
        position = new { board = p.Board, turn = side, ply = p.Ply, quietKingPlyCount = p.QuietKingPlyCount, repetitionCount = repetitions },
        legalMoves = legal.Select(m => new { id = m.Id, from = m.From+1, to = m.To+1, path = m.Path.Select(s => s+1), captures = m.Captures.Select(s => s+1), promotes = m.Promotes }),
        recentMoves = history.TakeLast(16), thinkTimeMs = player.Id == "kingsrow" ? 300 : 1000
    };
    using var response = await http.PostAsJsonAsync(player.Endpoint, request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{player.Id}: HTTP {response.StatusCode}: {text}");
    var answer = JsonSerializer.Deserialize<Answer>(text, json);
    return answer?.ProtocolVersion == "dama-model/v1" && legal.Find(m => m.Id == answer.MoveId) is { } found ? found
        : throw new InvalidOperationException($"Risposta illegale: {player.Id}: {text}");
}

if (args.Contains("--check"))
{
    // Synthetic positions only; no games are created and no result is recorded.
    var fixtures = new List<Position> { Position.Initial(), Position.Initial() with { Turn = -1 } };
    foreach (var pieces in new[] {
        new[] { (21, 1), (17, -1), (9, -1) },
        new[] { (9, 2), (13, -1), (22, -2) },
        new[] { (5, 1), (28, -1) },
        new[] { (21, 1), (17, -2), (1, -1) }
    }) {
        var board = new int[32]; foreach(var (square,piece) in pieces) board[square-1] = piece;
        fixtures.Add(new Position(board));
        fixtures.Add(new Position(board.Reverse().Select(x => -x).ToArray(), -1));
    }
    foreach (var p in fixtures) {
        var legal = Rules.LegalMoves(p);
        if (legal.Count == 0) throw new InvalidOperationException("Fixture senza mosse.");
        var selected = await Ask(kingsrow,p,legal,"fixture",[],1);
        Console.WriteLine($"fixture turn={p.Turn}, legal={legal.Count}: {selected.Id}");
    }
    foreach(var opponent in opponents) Console.WriteLine($"{opponent.Id}: {(await Ask(opponent,Position.Initial(),Rules.LegalMoves(Position.Initial()),"fixture",[],1)).Id}");
    return;
}
if (args.Length != 1) throw new ArgumentException("Uso: --check oppure directory nuova dei risultati");
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new InvalidOperationException("La directory risultati deve essere nuova: non sovrascrivere esperimenti.");
Directory.CreateDirectory(output);
var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
const int seed = 20261107;
const int limit = 400;
File.WriteAllText(Path.Combine(output,"manifest.json"), JsonSerializer.Serialize(new {
    startedAt = DateTimeOffset.UtcNow, seed, gamesPerOpponent = 18, openingPlies = 6, plyLimit = limit,
    kingsrowSearchMs = 200, opponentBudgetMs = 1000, kingsrow, opponents,
    competitionDirectory = store.DirectoryPath, rules = "italian-draughts-8x8",
    training = false, endgameDatabases = false, openingBook = "bundled; engine defaults", newSearchContextEveryMove = true
}, new JsonSerializerOptions(json) { WriteIndented = true }));
var results = new List<Result>();
using var stream = new StreamWriter(Path.Combine(output,"games.jsonl"));
foreach (var opponent in opponents)
for (var n = 0; n < 18; n++)
{
    var white = n % 2 == 0 ? kingsrow : opponent;
    var black = n % 2 == 0 ? opponent : kingsrow;
    using var tracked = store.Start("benchmark",Participant.Ai(white.Id,white.Name),Participant.Ai(black.Id,black.Name));
    var p = Position.Initial();
    var seen = new Dictionary<string,int> { [p.Key] = 1 };
    var history = new List<object>();
    var frames = new List<object> { new { ply = 0, board = p.Board, turn = p.Turn } };
    var random = new Random(seed + n / 2);
    Outcome? outcome = null;
    string? error = null;
    try {
        while (true) {
            var legal = Rules.LegalMoves(p);
            outcome = Rules.GetOutcome(p,seen.GetValueOrDefault(p.Key),legal);
            if (outcome is not null || p.Ply >= limit) break;
            var player = p.Turn == 1 ? white : black;
            var started = System.Diagnostics.Stopwatch.StartNew();
            var move = p.Ply < 6 ? legal[random.Next(legal.Count)] : await Ask(player,p,legal,tracked.Id,history,seen.GetValueOrDefault(p.Key));
            var elapsedMs = started.Elapsed.TotalMilliseconds;
            history.Add(new { ply = p.Ply + 1, player = p.Turn == 1 ? "white" : "black", moveId = move.Id });
            p = Rules.Apply(p,move);
            seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
            frames.Add(new { ply = p.Ply, board = p.Board, turn = p.Turn, moveId = move.Id, elapsedMs, opening = p.Ply <= 6 });
        }
    } catch(Exception e) { error = e.Message; }
    if (outcome is not null) tracked.Complete(outcome.Winner,p.Ply,outcome.Reason);
    else tracked.Interrupt(p.Ply,error is null ? "ply-limit" : "model-failure");
    var result = new Result(tracked.Id,opponent.Id,n+1,seed+n/2,white.Id,black.Id,outcome is not null,outcome?.Winner,outcome?.Reason ?? (error is null ? "ply-limit" : "model-failure"),p.Ply,error,frames);
    results.Add(result);
    stream.WriteLine(JsonSerializer.Serialize(result,json)); await stream.FlushAsync();
    File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(opponents.Select(o => new {
        opponent = o.Id, played = results.Count(r => r.Opponent == o.Id),
        wins = results.Count(r => r.Opponent == o.Id && r.Completed && r.Winner != 0 && (r.Winner == 1 ? r.White : r.Black) == "kingsrow"),
        draws = results.Count(r => r.Opponent == o.Id && r.Completed && r.Winner == 0),
        losses = results.Count(r => r.Opponent == o.Id && r.Completed && r.Winner != 0 && (r.Winner == 1 ? r.White : r.Black) != "kingsrow"),
        interrupted = results.Count(r => r.Opponent == o.Id && !r.Completed)
    }),new JsonSerializerOptions(json) { WriteIndented = true }));
    Console.WriteLine($"{results.Count}/54 {white.Id}-{black.Id}: {(outcome is null ? "interrotta" : outcome.Winner == 0 ? "patta" : outcome.Winner == 1 ? white.Id : black.Id)} ({p.Ply} ply) {error}");
    if (error is not null) throw new InvalidOperationException("Torneo fermato per errore del modello; conservati registro e traiettoria. " + error);
}

record Player(string Id,string Name,string Version,string Sha256,string Endpoint);
record Answer(string ProtocolVersion,string MoveId);
record Result(string MatchId,string Opponent,int Number,int OpeningSeed,string White,string Black,bool Completed,int? Winner,string Reason,int Plies,string? Error,List<object> Frames);
