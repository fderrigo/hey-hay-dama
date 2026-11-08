using System.Text.Json;
using Dama.Engine;
using Diaspro;

if (args is ["--write-weights", var path])
{
    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllBytes(fullPath, ManualWeights.Serialize(ManualWeights.Build()));
    return;
}

if (args is ["--profile", var milliseconds])
{
    var profiledNetwork = Network.Load($"models/Diaspro/Models/{ManualWeights.Version}.json");
    var position = Position.Initial();
    var result = Search.Evaluate(profiledNetwork, position, Rules.LegalMoves(position), milliseconds: int.Parse(milliseconds));
    Console.WriteLine(result);
    return;
}

if (args is ["--profile-bitboard", var bitboardMilliseconds])
{
    var profiledNetwork = Network.Load($"models/Diaspro/Models/{ManualWeights.Version}.json");
    var position = Position.Initial();
    var legal = Rules.LegalMoves(position);
    Console.WriteLine("v12 " + Search.Evaluate(profiledNetwork, position, legal, milliseconds: int.Parse(bitboardMilliseconds)));
    Console.WriteLine("bitboard " + DeepSearch.Evaluate(profiledNetwork, position, legal, milliseconds: int.Parse(bitboardMilliseconds)));
    return;
}

if (args is ["--profile-hints", var hintMilliseconds])
{
    var profiledNetwork = Network.Load($"models/Diaspro/Models/{ManualWeights.Version}.json");
    var position = Position.Initial();
    var legal = Rules.LegalMoves(position);
    var profileMemory = new PositionMemory();
    for (var iteration = 0; iteration < 3; iteration++)
        Console.WriteLine($"hints-{iteration} " + DeepSearch.Evaluate(profiledNetwork, position, legal, profileMemory, "profile",
            int.Parse(hintMilliseconds)));
    return;
}

if (args is ["--profile-fixed", var fixedDepth])
{
    var profiledNetwork = Network.Load($"models/Diaspro/Models/{ManualWeights.Version}.json");
    var position = Position.Initial();
    Console.WriteLine(DeepSearch.Evaluate(profiledNetwork, position, Rules.LegalMoves(position), milliseconds: 100000,
        maximumDepth: int.Parse(fixedDepth)));
    return;
}

if (args is ["--profile-leaves", var leafCount])
{
    var profiledNetwork = Network.Load($"models/Diaspro/Models/{ManualWeights.Version}.json");
    var random = new Random(18018);
    var positions = new List<Position>();
    for (var test = 0; test < int.Parse(leafCount); test++)
    {
        var board = new int[32];
        for (var count = random.Next(2, 20); count > 0; count--)
            board[random.Next(32)] = (random.Next(2) == 0 ? 1 : -1) * random.Next(1, 3);
        var position = new Position(board, test % 2 == 0 ? 1 : -1, QuietKingPlyCount: test % 90);
        if (FastRules.LegalMoves(position).Count > 0) positions.Add(position);
    }
    var checksum = 0f;
    var started = System.Diagnostics.Stopwatch.StartNew();
    foreach (var position in positions) checksum += profiledNetwork.EvaluateFast(position, FastRules.LegalMoves(position));
    Console.WriteLine($"array positions={positions.Count} elapsedMs={started.ElapsedMilliseconds} checksum={checksum}");
    started.Restart();
    foreach (var position in positions) checksum += profiledNetwork.EvaluateFast(BitPosition.FromPosition(position), BitRules.LegalMoves(BitPosition.FromPosition(position)));
    Console.WriteLine($"bits positions={positions.Count} elapsedMs={started.ElapsedMilliseconds} checksum={checksum}");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();
var network = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", ManualWeights.Version + ".json"));
var memory = new PositionMemory();

app.MapGet("/health", () => Results.Ok(new { status = "ok", protocolVersion = Protocol.Version, model = network.Name,
    architecture = "relational-graph-square-near-patterns", inputs = network.InputCount, parameters = network.ParameterCount,
    search = "bitboard-pvs-tt-native-leaves-move-hints", training = false }));
app.MapPost("/choose-move", async (HttpContext context) =>
{
    if (!context.Request.HasJsonContentType()) return Results.Json(new { error = "Il corpo deve essere application/json." }, statusCode: 415);
    ChooseMoveRequest? request;
    try { request = await JsonSerializer.DeserializeAsync<ChooseMoveRequest>(context.Request.Body, Protocol.Json, context.RequestAborted); }
    catch (JsonException) { return Results.Json(new { error = "JSON non valido." }, statusCode: 400); }
    catch (BadHttpRequestException ex) { return Results.Json(new { error = "Richiesta troppo grande o interrotta." }, statusCode: ex.StatusCode); }
    catch (OperationCanceledException) { return Results.Empty; }
    var problem = Protocol.Validate(request);
    if (problem is not null) return Results.Json(new { error = problem }, statusCode: 400);
    var position = Protocol.ToPosition(request!);
    var legal = Rules.LegalMoves(position);
    var budget = Math.Clamp(request!.ThinkTimeMs - 100, 50, 1000);
    var result = DeepSearch.Evaluate(network, position, legal, memory, request.GameId, budget, request.Position!.RepetitionCount);
    var chosen = legal[result.MoveIndex];
    memory.Commit(request.GameId, position, Rules.Apply(position, chosen));
    Console.WriteLine($"game={request.GameId} depth={result.CompletedDepth} nodes={result.Nodes} elapsedMs={result.ElapsedMilliseconds}");
    var supplied = request.LegalMoves!.First(m => chosen.Path.Select(s => s + 1).SequenceEqual(m.Path!));
    return Results.Json(new ChooseMoveResponse(Protocol.Version, supplied.Id!,
        $"PVS bitboard/TT profondità {result.CompletedDepth}, {result.Nodes} nodi; valore da autopartite, grafo SIMD e pattern simmetrici."), Protocol.Json);
});
app.Run();

public partial class Program;
