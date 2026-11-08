using System.Collections.Concurrent;
using System.Text.Json;
using Avventurina.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();
var margin = app.Configuration.GetValue("Avventurina:MarginMs", 100);
var tablebase = app.Configuration.GetValue("Avventurina:Tablebase", true) ? Tablebase.LoadOrBuild(app.Configuration["Avventurina:TablebaseDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "tablebase"), app.Configuration.GetValue("Avventurina:TablebasePieces", 4), Console.WriteLine) : null;
var tunedPath = app.Configuration["Avventurina:TunedWeights"];
if (!string.IsNullOrEmpty(tunedPath)) Tuned.Load(tunedPath);
var threads = app.Configuration.GetValue("Avventurina:Threads", 1);
var parallel = new ParallelSearcher(threads, app.Configuration.GetValue("Avventurina:TableBits", 21), tablebase);
var searcher = parallel.All[0];
searcher.NullMove = app.Configuration.GetValue("Avventurina:NullMove", searcher.NullMove);
searcher.Futility = app.Configuration.GetValue("Avventurina:Futility", searcher.Futility);
searcher.LogLmr = app.Configuration.GetValue("Avventurina:LogLmr", searcher.LogLmr);
searcher.SacrificeQs = app.Configuration.GetValue("Avventurina:SacrificeQs", searcher.SacrificeQs);
searcher.Simplify = app.Configuration.GetValue("Avventurina:Simplify", searcher.Simplify);
searcher.SacPlies = app.Configuration.GetValue("Avventurina:SacPlies", searcher.SacPlies);
searcher.DrawValue = app.Configuration.GetValue("Avventurina:DrawValue", searcher.DrawValue);
searcher.IterationLimit = app.Configuration.GetValue("Avventurina:IterationLimit", searcher.IterationLimit);
Eval.Balance = app.Configuration.GetValue("Avventurina:Balance", Eval.Balance);
Eval.AdvanceScale = app.Configuration.GetValue("Avventurina:AdvanceScale", Eval.AdvanceScale);
Eval.KingOpening = app.Configuration.GetValue("Avventurina:KingOpening", Eval.KingOpening);
Eval.MobilityWeight = app.Configuration.GetValue("Avventurina:MobilityWeight", Eval.MobilityWeight);
Eval.SupportWeight = app.Configuration.GetValue("Avventurina:SupportWeight", Eval.SupportWeight);
Eval.StuckWeight = app.Configuration.GetValue("Avventurina:StuckWeight", Eval.StuckWeight);
Eval.IsolatedWeight = app.Configuration.GetValue("Avventurina:IsolatedWeight", Eval.IsolatedWeight);
Eval.EdgeWeight = app.Configuration.GetValue("Avventurina:EdgeWeight", Eval.EdgeWeight);
Eval.GuardScale = app.Configuration.GetValue("Avventurina:GuardScale", Eval.GuardScale);
Eval.CentreWeight = app.Configuration.GetValue("Avventurina:CentreWeight", Eval.CentreWeight);
foreach (var helper in parallel.All.Skip(1))
{
    helper.NullMove = searcher.NullMove; helper.Futility = searcher.Futility; helper.LogLmr = searcher.LogLmr;
    helper.SacrificeQs = searcher.SacrificeQs; helper.Simplify = searcher.Simplify; helper.SacPlies = searcher.SacPlies;
    helper.DrawValue = searcher.DrawValue; helper.IterationLimit = searcher.IterationLimit;
}
var variant = $"{(threads > 1 ? "x" + threads : "")}{(Tuned.Weights is not null ? "-T" + Path.GetFileNameWithoutExtension(tunedPath) : "")}{(searcher.NullMove ? "n" : "")}{(searcher.Futility > 0 ? "f" + searcher.Futility : "")}{(searcher.LogLmr ? "l" : "")}{(searcher.SacrificeQs ? "" : "-s")}{(searcher.Simplify > 0 ? "s" + searcher.Simplify : "")}{(Eval.Balance != 10 ? "b" + Eval.Balance : "")}{(Eval.AdvanceScale != 100 ? "v" + Eval.AdvanceScale : "")}{(Eval.KingOpening != 170 ? "k" + Eval.KingOpening : "")}{(Eval.MobilityWeight != 3 ? "m" + Eval.MobilityWeight : "")}{(Eval.SupportWeight != 3 ? "u" + Eval.SupportWeight : "")}{(Eval.StuckWeight != 8 ? "t" + Eval.StuckWeight : "")}{(Eval.CentreWeight != 5 ? "c" + Eval.CentreWeight : "")}{(Eval.IsolatedWeight != 0 ? "o" + Eval.IsolatedWeight : "")}{(Eval.EdgeWeight != 0 ? "e" + Eval.EdgeWeight : "")}{(Eval.GuardScale != 100 ? "g" + Eval.GuardScale : "")}{(searcher.DrawValue != 0 ? "d" + searcher.DrawValue : "")}{(searcher.SacPlies != 0 ? "q" + searcher.SacPlies : "")}{(searcher.IterationLimit != 0.7 ? "i" + (int)(searcher.IterationLimit * 100) : "")}";
var gate = new SemaphoreSlim(1, 1); // one search at a time; Avventurina:Threads > 1 is a declared different budget
var games = new ConcurrentDictionary<string, Memory>();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/health", () => Results.Ok(new { status = "ok", protocolVersion = "dama-model/v1", model = "Avventurina", version = EngineInfo.Name + variant, tablebasePieces = tablebase?.MaxPieces ?? 0 }));

app.MapPost("/choose-move", async (HttpContext context) =>
{
    Request? r;
    try { r = await JsonSerializer.DeserializeAsync<Request>(context.Request.Body, json, context.RequestAborted); }
    catch (JsonException) { return Results.BadRequest(new { error = "JSON non valido." }); }
    if (r?.ProtocolVersion != "dama-model/v1" || r.Rules != "italian-draughts-8x8" || r.Player is not ("white" or "black")
        || r.Position?.Board is not { Length: 32 } board || board.Any(v => v is < -2 or > 2) || r.Position.Turn != r.Player
        || r.LegalMoves is not { Length: > 0 } legal || r.ThinkTimeMs <= 0)
        return Results.BadRequest(new { error = "Richiesta non valida." });

    var side = r.Player == "white" ? 1 : -1;
    var root = Pos.FromBoard(board, side, r.Position.QuietKingPlyCount);
    var id = r.GameId ?? "";
    var memory = games.GetOrAdd(id, _ => new Memory());
    await gate.WaitAsync(context.RequestAborted);
    try
    {
        // a new game (or a restarted one) starts with clean caches
        if (memory.Moves == 0 || r.Position.Ply < memory.LastPly) { parallel.Clear(); memory.Keys.Clear(); }
        var result = parallel.Search(root, memory.Keys, Math.Clamp(r.ThinkTimeMs - margin, 20, 10000));
        var chosen = legal.FirstOrDefault(m => m.From - 1 == result.Best.From && m.To - 1 == result.Best.To
            && Mask(m.Captures) == result.Best.Captured);
        if (chosen is null)
            return Results.Json(new { error = $"Mossa interna {result.Best.From + 1}-{result.Best.To + 1} assente dai candidati." }, statusCode: 500);
        memory.Keys.Add(root.Key);
        memory.Keys.Add(MoveGen.Make(root, result.Best).Key);
        memory.Moves++;
        memory.LastPly = r.Position.Ply;
        if (games.Count > 64) foreach (var k in games.Keys.Where(k => k != id).Take(games.Count - 64)) games.TryRemove(k, out _);
        return Results.Json(new
        {
            protocolVersion = "dama-model/v1", moveId = chosen.Id,
            comment = $"Avventurina {EngineInfo.Name}{variant}: profondità {result.Depth}, valutazione {result.Score}, nodi {result.Nodes}, {result.Ms:F0} ms {result.Note}".Trim()
        }, json);
    }
    finally { gate.Release(); }
});

app.Run();

static uint Mask(int[]? squares) => squares?.Aggregate(0u, (m, s) => m | 1u << (s - 1)) ?? 0;

sealed class Memory { public List<ulong> Keys { get; } = []; public int Moves; public int LastPly; }
record PositionDto(int[]? Board, string? Turn, int Ply, int QuietKingPlyCount, int RepetitionCount);
record MoveDto(string Id, int From, int To, int[]? Path, int[]? Captures, bool Promotes);
record Request(string? ProtocolVersion, string? Rules, string? RequestId, string? GameId, string? Player, PositionDto? Position, MoveDto[]? LegalMoves, int ThinkTimeMs);
