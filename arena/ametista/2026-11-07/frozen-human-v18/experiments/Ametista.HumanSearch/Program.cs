using System.Security.Cryptography;
using System.Text.Json;
using Ametista;
using Dama.Engine;

if(args is ["--verify-human-search",var manualFile,var humanFile]) {
    // Resolve user paths before locating the repository for existing replay fixtures.
    string manualPath=Path.GetFullPath(manualFile),humanModelPath=Path.GetFullPath(humanFile);
    var repository=new DirectoryInfo(AppContext.BaseDirectory);
    while(repository is not null&&!File.Exists(Path.Combine(repository.FullName,"WeightSpaceGames.slnx")))repository=repository.Parent;
    if(repository is null)throw new InvalidOperationException("Repository root required for replay fixtures.");
    Directory.SetCurrentDirectory(repository.FullName);
    byte[] verificationManualBytes=File.ReadAllBytes(manualPath);
    string verificationManualSha=Convert.ToHexStringLower(SHA256.HashData(verificationManualBytes));
    if(verificationManualSha!="7e8ea1c6a7c897e5d3d5c42a43511a26ad0ebd32db904fff670be488c7913138")
        throw new InvalidDataException("Verification requires the frozen manual-v4 model.");
    var manualDefinition=JsonSerializer.Deserialize<Weights>(verificationManualBytes,Network.JsonOptions)
        ?? throw new InvalidDataException("Missing manual weights.");
    var verificationHuman=HumanOutcome.Load(humanModelPath);
    // ONLY metadata changes: preserve every numerical parameter and encoder input.
    // Version-specific assertions concern the manual-only bonus, not this sum.
    var verificationNetwork=new Network(manualDefinition with {Version="manual-v4+human-verification"},verificationHuman);
    Verification.Run(verificationNetwork,verificationManualSha,verificationHuman.Sha256);
    return;
}
if(args is ["--verify-human"]) { Console.WriteLine(JsonSerializer.Serialize(HumanVerification.Run())); return; }
if(args is ["--predict-human",var modelFile,var fixtureFile]) {
    var model=HumanOutcome.Load(modelFile);
    var fixtures=JsonSerializer.Deserialize<HumanFixture[]>(File.ReadAllText(fixtureFile),Network.JsonOptions)
        ?? throw new InvalidDataException("Missing fixture array.");
    var results=fixtures.Select((f,index)=> {
        if(f.Board is not {Length:32} || f.Turn is not(1 or -1) || f.Board.Any(p=>p is < -2 or >2))
            throw new InvalidDataException("Invalid fixture board/turn.");
        double prediction=model.Predict(Board.From(new Position(f.Board,f.Turn)));
        return new {index,prediction,contribution=HumanOutcome.ContributionScale*prediction};
    }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new {model.Sha256,scale=HumanOutcome.ContributionScale,results}));return;
}
var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=64*1024);
var weightsPath=builder.Configuration["weights"]??Path.Combine(AppContext.BaseDirectory,"Models","ametista-v4.json");
var humanPath=builder.Configuration["human-weights"] ?? throw new InvalidOperationException("Explicit --human-weights is required; no fallback model.");
var manualBytes=File.ReadAllBytes(weightsPath);
var sha=Convert.ToHexStringLower(SHA256.HashData(manualBytes));
if(sha!="7e8ea1c6a7c897e5d3d5c42a43511a26ad0ebd32db904fff670be488c7913138")
    throw new InvalidDataException("Only frozen manual-v4 weights may be combined.");
var human=HumanOutcome.Load(humanPath);
var network=new Network(JsonSerializer.Deserialize<Weights>(manualBytes,Network.JsonOptions) ?? throw new InvalidDataException("Missing manual network."),human);
var search=new Search(network);
var gate=new object();
var gameHistory=new Dictionary<string,SortedDictionary<int,ulong>>();
var app=builder.Build();
app.MapGet("/health",()=>new {status="ok",model="Ametista Human Outcome",version="manual-v4+human-linear-v1",manualVersion=network.Definition.Version,sha256=sha,manualSha256=sha,humanSha256=human.Sha256,humanVersion=human.Definition.Name,humanContributionScale=HumanOutcome.ContributionScale,search=Search.Version,architecture="original-v14-plus-human-outcome",threads=1,compiledInference=network.CompiledInference});
app.MapPost("/choose-move",(Request r,CancellationToken requestAborted)=> {
    if(r.ProtocolVersion!="dama-model/v1"||r.Rules!="italian-draughts-8x8"||r.Player is not("white" or "black")
        ||r.Position?.Board is not {Length:32}||r.Position.Turn!=r.Player||r.Position.Board.Any(x=>x is < -2 or >2)
        ||r.Position.Board.Count(x=>x>0)>12||r.Position.Board.Count(x=>x<0)>12||r.Position.Ply<0||r.Position.QuietKingPlyCount<0
        ||r.LegalMoves is not {Length:>0 and <=256}||r.ThinkTimeMs is <100 or >30000||string.IsNullOrWhiteSpace(r.GameId)||r.GameId.Length>128)
        return Results.BadRequest(new {error="Invalid request."});
    var position=new Position(r.Position.Board,r.Player=="white"?1:-1,r.Position.Ply,r.Position.QuietKingPlyCount);
    var legal=Rules.LegalMoves(position);
    if(r.LegalMoves.Any(m=>m is null||string.IsNullOrWhiteSpace(m.Id)||m.Path is null||!legal.Any(l=>l.Path.Select(s=>s+1).SequenceEqual(m.Path))))
        return Results.BadRequest(new {error="Invalid legal moves."});
    bool lockTaken=false;
    try {
        Monitor.TryEnter(gate,ref lockTaken);
        if(!lockTaken)return Results.Json(new {error="Ametista is busy. Retry after the active search."},statusCode:503);
        requestAborted.ThrowIfCancellationRequested();
        if(!gameHistory.TryGetValue(r.GameId,out var timeline)) {
            if(gameHistory.Count>=128) gameHistory.Remove(gameHistory.Keys.First());
            gameHistory[r.GameId]=timeline=[];
        }
        foreach(var ply in timeline.Keys.Where(p=>p>=position.Ply).ToArray()) timeline.Remove(ply);
        var board=Board.From(position); timeline[position.Ply]=board.Key;
        var seen=timeline.Values.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
        seen[board.Key]=Math.Max(seen[board.Key],r.Position.RepetitionCount);
        var decision=search.Choose(board,r.GameId,Math.Max(10,r.ThinkTimeMs-100),seen,requestAborted);
        requestAborted.ThrowIfCancellationRequested();
        var after=board.Apply(decision.Move);
        var chosen=legal.FirstOrDefault(m=>Board.From(Rules.Apply(position,m))==after);
        var supplied=chosen is null?null:r.LegalMoves.FirstOrDefault(m=>chosen.Path.Select(x=>x+1).SequenceEqual(m.Path));
        if(supplied is null) return Results.UnprocessableEntity(new {error="Search and referee disagree."});
        timeline[position.Ply+1]=after.Key;
        return Results.Ok(new {protocolVersion="dama-model/v1",moveId=supplied.Id,
            comment=$"Ametista Human {Search.Version}: depth {decision.Depth}, nodes {decision.Nodes}, score {decision.Score}.",
            diagnostics=new {decision.Depth,decision.Nodes,decision.Score,decision.Risk,decision.Milliseconds,decision.CacheHits,decision.PreviousTurnCacheHits,decision.EvaluationCacheHits,decision.AspirationRetries,decision.LateReductions,decision.ReductionResearches}});
    } finally {if(lockTaken)Monitor.Exit(gate);}
});
app.Run();

record PositionDto(int[] Board,string Turn,int Ply,int QuietKingPlyCount,int RepetitionCount);
record MoveDto(string Id,int[] Path);
record Request(string ProtocolVersion,string Rules,string GameId,string Player,PositionDto Position,MoveDto[] LegalMoves,int ThinkTimeMs);

record HumanFixture(int[] Board,int Turn);
