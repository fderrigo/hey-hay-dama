using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

if (args.Length == 0 || !int.TryParse(args[0], out var round) || round is not (1 or 2))
    throw new ArgumentException("Uso: Torneo <1=diagnosi|2=conferma> [gamesPerPair] [--check]");
var games = args.Length > 1 && int.TryParse(args[1], out var g) ? g : round == 1 ? 6 : 98;
if (games <= 0 || games % 2 != 0) throw new ArgumentException("Il numero di partite per coppia deve essere positivo e pari.");
var checkOnly = args.Contains("--check");
var root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "WeightSpaceGames.slnx"))) root = Path.GetDirectoryName(root)!;
var coordination = Path.Combine(root, "arena", "coordinamento");
var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var competition = new CompetitionStore(CompetitionStore.DefaultDirectory());
var entries = Directory.GetFiles(Path.Combine(coordination, "iscrizioni"), "AGENTE-*.json").OrderBy(p => p, StringComparer.Ordinal)
    .Select(p => JsonSerializer.Deserialize<Entry>(File.ReadAllText(p), web)!).ToArray();
if (entries.Length != 3 || entries.Select(e => e.Id).Distinct().Count() != 3) throw new InvalidDataException("Servono esattamente tre iscrizioni con ID distinti.");
var field = entries.Select(e => new Player(e.Id, e.Name, e.Version, e.Sha256, e.Endpoint)).ToArray();
var seed = round == 1 ? 731941 : BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(round + "|" + string.Join("|", entries.Select(e => e.Sha256.ToLowerInvariant())))), 0) & 0x3fffffff;
var startUnix = long.Parse(File.ReadAllText(Path.Combine(coordination, "START")));
var deadline = DateTimeOffset.FromUnixTimeSeconds(startUnix + (round == 1 ? 20 : 58) * 60);
var roundName = round == 1 ? "diagnosi" : "conferma";
var outputDirectory = Path.Combine(coordination, "risultati", roundName);
Directory.CreateDirectory(outputDirectory);
Console.WriteLine($"Girone {roundName}, seed {seed}, scadenza {deadline:HH:mm:ss}: {string.Join(", ", field.Select(f => f.Id + ":" + f.Version))}");

static object View(Move m) => new { id = m.Id, from = m.From + 1, to = m.To + 1, path = m.Path.Select(s => s + 1), captures = m.Captures.Select(s => s + 1), promotes = m.Promotes };

async Task<Move?> Ask(Player player, Position p, List<Move> legal, string game, List<object> history, int repetitions)
{
    var colour = p.Turn == 1 ? "white" : "black";
    var body = new
    {
        protocolVersion = "dama-model/v1", rules = "italian-draughts-8x8", requestId = Guid.NewGuid().ToString("N"), gameId = game, player = colour,
        position = new { board = p.Board, turn = colour, ply = p.Ply, quietKingPlyCount = p.QuietKingPlyCount, repetitionCount = repetitions },
        legalMoves = legal.Select(View), recentMoves = history.TakeLast(16), thinkTimeMs = 3000,
    };
    try
    {
        using var response = await http.PostAsJsonAsync(player.Endpoint, body);
        if (!response.IsSuccessStatusCode) return null;
        var answer = await response.Content.ReadFromJsonAsync<Answer>(web);
        return answer?.ProtocolVersion == "dama-model/v1" ? legal.FirstOrDefault(m => m.Id == answer.MoveId) : null;
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { return null; }
}

if (checkOnly)
{
    var p = Position.Initial();
    var legal = Rules.LegalMoves(p);
    foreach (var player in field)
        Console.WriteLine($"{player.Id}:{player.Version}: {(await Ask(player, p, legal, Guid.NewGuid().ToString("N"), [], 1))?.Id ?? "NESSUNA RISPOSTA VALIDA"}");
    return;
}

async Task<Result> Game(Player white, Player black, int gameSeed)
{
    var r = new Random(gameSeed);
    using var tracked = competition.Start("benchmark", Participant.Ai(white.Id, white.Name), Participant.Ai(black.Id, black.Name));
    var p = Position.Initial();
    var seen = new Dictionary<string, int> { [p.Key] = 1 };
    var history = new List<object>();
    var frames = new List<Frame>();
    Outcome? outcome = null;
    var interruption = string.Empty;
    while (p.Ply < 240)
    {
        var legal = Rules.LegalMoves(p);
        outcome = Rules.GetOutcome(p, seen.GetValueOrDefault(p.Key), legal);
        if (outcome is not null) break;
        if (DateTimeOffset.UtcNow >= deadline) { interruption = "round-deadline"; break; }
        var side = p.Turn == 1 ? white : black;
        var move = p.Ply < 6 ? legal[r.Next(legal.Count)] : await Ask(side, p, legal, tracked.Id, history, seen.GetValueOrDefault(p.Key));
        if (move is null) { interruption = "model-failure"; break; }
        history.Add(new { ply = p.Ply + 1, player = p.Turn == 1 ? "white" : "black", moveId = move.Id });
        p = Rules.Apply(p, move);
        seen[p.Key] = seen.GetValueOrDefault(p.Key) + 1;
        frames.Add(new Frame(p.Ply, move.Id, p.Board, p.Turn));
    }
    if (outcome is null && p.Ply >= 240)
    {
        outcome = Rules.GetOutcome(p, seen.GetValueOrDefault(p.Key), Rules.LegalMoves(p));
        if (outcome is null) interruption = "ply-limit";
    }
    if (outcome is not null) tracked.Complete(outcome.Winner, p.Ply, outcome.Reason);
    else tracked.Interrupt(p.Ply, interruption);
    var balance = p.Board.Sum(x => x == 0 ? 0 : Math.Sign(x) * Math.Abs(x));
    return new Result(tracked.Id, outcome?.Winner ?? 0, outcome is not null, outcome?.Reason ?? interruption, balance, p.Ply, p.Board.Count(x => Math.Abs(x) == 2), frames);
}

var lines = new List<string>
{
    $"# Girone {roundName}", "", $"Seed {seed}; {games} partite pianificate per coppia; esecuzione sequenziale.", "",
    "ID stabili nel registro; versioni e SHA256 qui e nelle traiettorie. Punteggio 2/1/0.", "",
    "| Coppia | Versioni | V | P | S | Punti primo | Interrotte | Errori modello |", "| --- | --- | --- | --- | --- | --- | --- | --- |"
};
var table = field.ToDictionary(f => f.Id, _ => new int[5]);
var drawnAhead = new Dictionary<string, int>();
var complete = true;
using var trajectories = new StreamWriter(Path.Combine(outputDirectory, "traiettorie.jsonl"));
var pairIndex = 0;
for (var i = 0; i < field.Length; i++)
for (var j = i + 1; j < field.Length; j++)
{
    var a = field[i];
    var b = field[j];
    var results = new List<(Result Result, bool AWhite)>();
    for (var n = 0; n < games; n++)
    {
        if (DateTimeOffset.UtcNow >= deadline) break;
        var aWhite = n % 2 == 0;
        var gameSeed = seed + pairIndex * 1009 + n / 2;
        var result = aWhite ? await Game(a, b, gameSeed) : await Game(b, a, gameSeed);
        results.Add((result, aWhite));
        trajectories.WriteLine(JsonSerializer.Serialize(new
        {
            result.MatchId, gameSeed, white = aWhite ? a.Id : b.Id, black = aWhite ? b.Id : a.Id,
            whiteVersion = aWhite ? a.Version : b.Version, blackVersion = aWhite ? b.Version : a.Version,
            whiteSha256 = aWhite ? a.Sha256 : b.Sha256, blackSha256 = aWhite ? b.Sha256 : a.Sha256,
            result.Completed, result.Winner, result.Reason, result.Plies, result.Frames
        }, web));
        await trajectories.FlushAsync();
    }
    complete &= results.Count == games && results.All(x => x.Result.Completed);
    var w = results.Count(x => x.Result.Completed && x.Result.Winner != 0 && (x.Result.Winner == 1) == x.AWhite);
    var l = results.Count(x => x.Result.Completed && x.Result.Winner != 0 && (x.Result.Winner == 1) != x.AWhite);
    var d = results.Count(x => x.Result.Completed && x.Result.Winner == 0);
    var interruptions = results.Count(x => !x.Result.Completed);
    var failures = results.Count(x => x.Result.Reason == "model-failure");
    foreach (var x in results.Where(x => x.Result.Completed && x.Result.Winner == 0 && x.Result.Balance != 0))
    {
        var leader = (x.Result.Balance > 0) == x.AWhite ? a.Id : b.Id;
        drawnAhead[leader] = drawnAhead.GetValueOrDefault(leader) + 1;
    }
    lines.Add($"| {a.Id} – {b.Id} | {a.Version} – {b.Version} | {w} | {d} | {l} | {2 * w + d} | {interruptions} | {failures} |");
    Console.WriteLine(lines[^1]);
    table[a.Id][0] += w; table[a.Id][1] += d; table[a.Id][2] += l; table[a.Id][3] += interruptions; table[a.Id][4] += failures;
    table[b.Id][0] += l; table[b.Id][1] += d; table[b.Id][2] += w; table[b.Id][3] += interruptions; table[b.Id][4] += failures;
    pairIndex++;
}
lines.AddRange(["", "| Motore | Versione | V | P | S | Punti | Interrotte | Errori modello | Patte in vantaggio |", "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]);
foreach (var player in field)
{
    var t = table[player.Id];
    lines.Add($"| {player.Id} | {player.Version} | {t[0]} | {t[1]} | {t[2]} | {2 * t[0] + t[1]} | {t[3]} | {t[4]} | {drawnAhead.GetValueOrDefault(player.Id)} |");
}
lines.AddRange(["", $"Stato: {(complete ? "COMPLETO" : "INCOMPLETO")}.", $"Traiettorie: `{Path.Combine(outputDirectory, "traiettorie.jsonl")}`."]);
File.WriteAllLines(Path.Combine(outputDirectory, "verbale.md"), lines);
Console.WriteLine($"Verbale: {Path.Combine(outputDirectory, "verbale.md")} — {(complete ? "COMPLETO" : "INCOMPLETO")}");

internal sealed record Entry(string Id, string Name, string Version, string Endpoint, string Sha256);
internal sealed record Answer(string? ProtocolVersion, string? MoveId);
internal sealed record Player(string Id, string Name, string Version, string Sha256, string Endpoint);
internal sealed record Frame(int Ply, string MoveId, int[] Board, int Turn);
internal sealed record Result(string MatchId, int Winner, bool Completed, string Reason, int Balance, int Plies, int Kings, List<Frame> Frames);
