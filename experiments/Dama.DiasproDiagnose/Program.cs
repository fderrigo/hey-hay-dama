using System.Text.Json;
using Dama.Engine;
using Diaspro;

if (args.Length == 2 && args[0] == "--snapshot")
{
    var snapshotNetwork = Network.Load("models/Diaspro/Models/diaspro-v4.json");
    var random = new Random(19);
    var rows = new List<object>();
    for (var test = 0; test < 200; test++)
    {
        var board = new int[32];
        for (var n = random.Next(2, 20); n > 0; n--) board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
        var position = new Position(board, test % 2 == 0 ? 1 : -1, test, test % 70);
        rows.Add(new { position.Board, position.Turn, position.Ply, position.QuietKingPlyCount,
            Value = snapshotNetwork.Evaluate(position), Features = snapshotNetwork.Features(position) });
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return;
}
if (args is ["--position", var positionDirectory, var gameIndexText, var plyText])
{
    var positionNetwork = Network.Load("models/Diaspro/Models/diaspro-v4.json");
    var gameIndex = int.Parse(gameIndexText);
    var targetPly = int.Parse(plyText);
    using var document = JsonDocument.Parse(File.ReadLines(Path.Combine(positionDirectory, "games.jsonl")).Skip(gameIndex).First());
    var position = Position.Initial();
    foreach (var frame in document.RootElement.GetProperty("frames").EnumerateArray().Skip(1))
    {
        if (position.Ply >= targetPly) break;
        var move = Rules.LegalMoves(position).Single(m => m.Id == frame.GetProperty("moveId").GetString());
        position = Rules.Apply(position, move);
    }
    if (position.Ply != targetPly) throw new ArgumentException("Ply non raggiungibile.");
    var legal = Rules.LegalMoves(position);
    Console.WriteLine($"ply={position.Ply}, turn={position.Turn}, board={string.Join(',', position.Board)}");
    var values = Policy.Values(positionNetwork, position, legal);
    for (var index = 0; index < legal.Count; index++)
    {
        var next = Rules.Apply(position, legal[index]);
        Console.WriteLine($"{index}: {legal[index].Id}, statica={values[index]:F3}, rischio={Risk(next):F0}, scambio={Exchange(next):F0}");
    }
    Console.WriteLine(Search.Evaluate(positionNetwork, position, legal, milliseconds: 10000, maximumDepth: 8));
    return;
}
if (args.Length != 1) throw new ArgumentException("Uso: directory, --initial-features, --snapshot percorso oppure --position directory indice ply.");
var network = Network.Load("models/Diaspro/Models/diaspro-v4.json");
if (args[0] == "--initial-features")
{
    var initial = Position.Initial();
    Console.WriteLine(string.Join(", ", network.Features(initial).Select((value, index) => $"{index}:{value}")));
    Console.WriteLine(network.Evaluate(initial));
    return;
}
var records = new List<object>();
var summaries = new List<object>();
var games = 0;
var decisions = 0;
var avoidable = 0;
var avoidableExchanges = 0;
foreach (var line in Lines(Path.Combine(args[0], "games.jsonl")))
{
    using var document = JsonDocument.Parse(line);
    var game = document.RootElement;
    var position = Position.Initial();
    var frames = game.GetProperty("frames").EnumerateArray().ToArray();
    foreach (var frame in frames.Skip(1))
    {
        var legal = Rules.LegalMoves(position);
        var move = legal.Single(m => m.Id == frame.GetProperty("moveId").GetString());
        var diasproTurn = position.Turn == 1 ? game.GetProperty("white").GetString() == "diaspro" : game.GetProperty("black").GetString() == "diaspro";
        if (diasproTurn && position.Ply >= 6)
        {
            var values = Policy.Values(network, position, legal);
            var risks = legal.Select(m => Risk(Rules.Apply(position, m))).ToArray();
            var exchanges = legal.Select(m => Exchange(Rules.Apply(position, m))).ToArray();
            var chosen = legal.FindIndex(m => m.Id == move.Id);
            var minimum = risks.Min();
            var minimumExchange = exchanges.Min();
            decisions++;
            if (risks[chosen] >= minimum + 100) avoidable++;
            if (exchanges[chosen] >= minimumExchange + 100) avoidableExchanges++;
            if (risks[chosen] >= minimum + 100 || exchanges[chosen] >= minimumExchange + 100)
            {
                records.Add(new
                {
                    matchId = game.GetProperty("matchId").GetString(), ply = position.Ply + 1, chosen = move.Id,
                    chosenRisk = risks[chosen], minimumRisk = minimum, chosenExchange = exchanges[chosen],
                    minimumExchange, chosenValue = values[chosen],
                    alternatives = legal.Select((m, i) => new { id = m.Id, value = values[i], risk = risks[i], exchange = exchanges[i] })
                        .OrderBy(x => x.exchange).ThenBy(x => x.risk).ThenByDescending(x => x.value).Take(4).ToArray()
                });
            }
        }
        position = Rules.Apply(position, move);
    }
    var side = game.GetProperty("white").GetString() == "diaspro" ? 1 : -1;
    var materials = frames.Select(frame => Network.Material(frame.GetProperty("board").EnumerateArray().Select(v => v.GetInt32()).ToArray(), side)).ToArray();
    var maximum = materials.Max();
    summaries.Add(new
    {
        matchId = game.GetProperty("matchId").GetString(), plies = position.Ply, finalMaterial = materials[^1],
        maximumMaterial = maximum, maximumPly = Array.IndexOf(materials, maximum), finalPieces = position.Board.Count(p => Math.Sign(p) == side),
        finalKings = position.Board.Count(p => p == 2 * side), result = game.GetProperty("reason").GetString()
    });
    games++;
}
var output = Path.Combine(args[0], "diagnosi-tattica.json");
File.WriteAllText(output, JsonSerializer.Serialize(new { games, decisions, avoidable, avoidableExchanges, summaries, records },
    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine($"partite={games}, decisioni={decisions}, rischio evitabile={avoidable}, scambi evitabili={avoidableExchanges}, output={output}");
foreach (var summary in summaries) Console.WriteLine(JsonSerializer.Serialize(summary));

static float Risk(Position position)
{
    var legal = Rules.LegalMoves(position);
    if (legal.Count == 0) return 20000;
    return legal.Where(m => m.Captures.Length > 0).Select(m => Capture(m)).DefaultIfEmpty(0).Max();
}

static float Exchange(Position position)
{
    var legal = Rules.LegalMoves(position);
    if (legal.Count == 0) return 20000;
    var worst = 0f;
    foreach (var capture in legal.Where(m => m.Captures.Length > 0))
    {
        var replyPosition = Rules.Apply(position, capture);
        var replies = Rules.LegalMoves(replyPosition);
        var recovery = replies.Count == 0 ? 0 : replies.Where(m => m.Captures.Length > 0).Select(m => Capture(m)).DefaultIfEmpty(0).Max();
        worst = Math.Max(worst, Capture(capture) - recovery);
    }
    return worst;
}

static float Capture(Move move) => move.VictimRanks.Sum(rank => rank == 2 ? 336 : 112) + (move.Promotes ? 224 : 0);

static IEnumerable<string> Lines(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is { } line) yield return line;
}
