using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Dama.Engine;
using Dama.Competition;

if(args.Length!=2) throw new ArgumentException("Usage: configuration.json new-output-directory");
var root=new DirectoryInfo(AppContext.BaseDirectory);
while(root is not null&&!File.Exists(Path.Combine(root.FullName,"WeightSpaceGames.slnx"))) root=root.Parent;
if(root is null) throw new InvalidOperationException("Repository not found.");
Directory.SetCurrentDirectory(root.FullName);
if(args[0]=="--audit") { ChallengeAudit.Run(args[1]); return; }
if(args[0]=="--web-check") { await WebIntegration.Check(args[1]); return; }
if(args[0]=="--interrupt-web-check") { WebIntegration.Interrupt(args[1]); return; }
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
var compact=new JsonSerializerOptions(JsonSerializerDefaults.Web);
var config=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(args[0]),json)!;
if(config.Seeds.Length<1||config.Source is not("test" or "benchmark")||config.A.Id==config.B.Id||config.A.SearchMs<100||config.B.SearchMs<100)
    throw new InvalidDataException("Invalid configuration.");
if(new[]{config.A,config.B}.Any(p=>p.ProtocolReserveMs is <0 or >1000||p.SearchMs+p.ProtocolReserveMs>30000))
    throw new InvalidDataException("Invalid protocol time reserve.");
if(config.Deadline<=DateTimeOffset.UtcNow) throw new InvalidOperationException("Deadline expired.");
var output=Path.GetFullPath(args[1]);
if(Directory.Exists(output)) throw new InvalidOperationException("Do not overwrite an experiment.");
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new { startedAt=DateTimeOffset.UtcNow,config,openingPlies=6,plyLimit=400,training=false },json));
using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(35)};
var store=new CompetitionStore(CompetitionStore.DefaultDirectory());
var lockPath=Path.GetFullPath(config.MatchLock??"arena/kingsrow-challenge/matches.lock");
Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
var results=new List<GameResult>();
using var trajectories=new StreamWriter(Path.Combine(output,"games.jsonl"));

foreach(var player in new[]{config.A,config.B}) {
    var healthUrl=new Uri(new Uri(player.Endpoint),"/health");
    var health=await http.GetStringAsync(healthUrl);
    using var doc=JsonDocument.Parse(health);
    if(doc.RootElement.TryGetProperty("sha256",out var hash)&&!string.Equals(hash.GetString(),player.Sha256,StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Health hash mismatch for {player.Id}.");
    if(player.ExpectedHealth is not null) foreach(var (field,expected) in player.ExpectedHealth)
        if(!doc.RootElement.TryGetProperty(field,out var actual)||actual.ValueKind!=JsonValueKind.String||actual.GetString()!=expected)
            throw new InvalidOperationException($"Health field {field} mismatch for {player.Id}.");
    File.WriteAllText(Path.Combine(output,$"health-{player.Id}.json"),health);
}

async Task<FileStream> Acquire()
{
    while(DateTimeOffset.UtcNow<config.Deadline) {
        try { return new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException) { await Task.Delay(500); }
    }
    throw new OperationCanceledException("Challenge deadline while waiting for shared match lock.");
}
long Peak(Player player)
{
    if(player.ProcessId<=0) return 0;
    using var p=Process.GetProcessById(player.ProcessId); p.Refresh();
    return p.PeakWorkingSet64;
}
async Task<(Move Move,JsonElement? Diagnostics,double Ms)> Ask(Player player,Position p,List<Move> legal,string game,List<object> history,int repetitions)
{
    var side=p.Turn==1?"white":"black";
    var request=new { protocolVersion="dama-model/v1",rules="italian-draughts-8x8",requestId=Guid.NewGuid().ToString("N"),gameId=game,player=side,
        position=new {board=p.Board,turn=side,ply=p.Ply,quietKingPlyCount=p.QuietKingPlyCount,repetitionCount=repetitions},
        legalMoves=legal.Select(m=>new {id=m.Id,from=m.From+1,to=m.To+1,path=m.Path.Select(x=>x+1),captures=m.Captures.Select(x=>x+1),promotes=m.Promotes}),
        recentMoves=history.TakeLast(16),thinkTimeMs=player.SearchMs+player.ProtocolReserveMs };
    var timer=Stopwatch.StartNew();
    using var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(player.SearchMs+2500));
    using var response=await http.PostAsJsonAsync(player.Endpoint,request,timeout.Token);
    var text=await response.Content.ReadAsStringAsync(timeout.Token);
    if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"{player.Id}: HTTP {(int)response.StatusCode}: {text}");
    var answer=JsonSerializer.Deserialize<Answer>(text,compact);
    var move=answer?.ProtocolVersion=="dama-model/v1"?legal.FirstOrDefault(m=>m.Id==answer.MoveId):null;
    if(move is null) throw new InvalidOperationException($"Illegal response from {player.Id}.");
    return (move,answer!.Diagnostics,timer.Elapsed.TotalMilliseconds);
}

foreach(var seed in config.Seeds)
for(int color=0;color<2;color++)
{
    if(DateTimeOffset.UtcNow>=config.Deadline) break;
    using(await Acquire()) {
        var white=color==0?config.A:config.B; var black=color==0?config.B:config.A;
        using var tracked=store.Start(config.Source,Participant.Ai(white.Id,white.Name),Participant.Ai(black.Id,black.Name));
        var p=Position.Initial();
        var seen=new Dictionary<string,int>{{p.Key,1}};
        var frames=new List<object>{new {ply=0,board=p.Board,turn=p.Turn}};
        var history=new List<object>();
        var random=new Random(seed); Outcome? outcome=null; string? error=null;
        string interruption="ply-limit";
        long whitePeak=0,blackPeak=0;
        try {
            while(true) {
                var legal=Rules.LegalMoves(p); outcome=Rules.GetOutcome(p,seen.GetValueOrDefault(p.Key),legal);
                if(outcome is not null||p.Ply>=400) break;
                if(DateTimeOffset.UtcNow>=config.Deadline) {interruption="deadline";break;}
                var player=p.Turn==1?white:black;
                Move move; JsonElement? diagnostics=null; double elapsed=0;
                if(p.Ply<6) move=legal[random.Next(legal.Count)];
                else (move,diagnostics,elapsed)=await Ask(player,p,legal,tracked.Id,history,seen[p.Key]);
                history.Add(new {ply=p.Ply+1,player=p.Turn==1?"white":"black",moveId=move.Id});
                p=Rules.Apply(p,move); seen[p.Key]=seen.GetValueOrDefault(p.Key)+1;
                frames.Add(new {ply=p.Ply,board=p.Board,turn=p.Turn,moveId=move.Id,opening=p.Ply<=6,elapsedMs=elapsed,diagnostics});
                if(p.Ply%10==0) {
                    whitePeak=Math.Max(whitePeak,Peak(white));blackPeak=Math.Max(blackPeak,Peak(black));
                    if(Math.Max(whitePeak,blackPeak)>1024L*1024*1024) {interruption="memory-limit";break;}
                }
            }
        } catch(Exception e) {error=e.Message;interruption="model-failure";}
        if(outcome is not null) tracked.Complete(outcome.Winner,p.Ply,outcome.Reason); else tracked.Interrupt(p.Ply,interruption);
        var result=new GameResult(tracked.Id,seed,white.Id,black.Id,outcome is not null,outcome?.Winner,outcome?.Reason??interruption,p.Ply,error,whitePeak,blackPeak,frames);
        results.Add(result);trajectories.WriteLine(JsonSerializer.Serialize(result,compact));await trajectories.FlushAsync();
        var wins=results.Count(r=>r.Completed&&r.Winner!=0&&(r.Winner==1?r.White:r.Black)==config.A.Id);
        var draws=results.Count(r=>r.Completed&&r.Winner==0);
        var losses=results.Count(r=>r.Completed&&r.Winner!=0&&(r.Winner==1?r.White:r.Black)!=config.A.Id);
        var summary=new {candidate=config.A.Id,games=results.Count,wins,draws,losses,interrupted=results.Count(r=>!r.Completed),points=2*wins+draws,score=(wins+.5*draws)/results.Count};
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summary,json));
        Console.WriteLine($"{results.Count}/{config.Seeds.Length*2} {white.Id}-{black.Id}: {(outcome is null?interruption:outcome.Winner==0?"draw":outcome.Winner==1?white.Id:black.Id)} ply={p.Ply}; A {wins}/{draws}/{losses}; {error}");
        if(error is not null) throw new InvalidOperationException("Stopped; failed game remains registered: "+error);
    }
    // Let another waiting agent acquire the common lock between games.
    await Task.Delay(250);
}

record Player(string Id,string Name,string Version,string Sha256,string Endpoint,int SearchMs,int ProcessId,int ProtocolReserveMs=100,Dictionary<string,string>? ExpectedHealth=null);
record Configuration(Player A,Player B,int[] Seeds,string Source,DateTimeOffset Deadline,string? MatchLock,int? LogicalProcessor);
record Answer(string ProtocolVersion,string MoveId,JsonElement? Diagnostics);
record GameResult(string MatchId,int OpeningSeed,string White,string Black,bool Completed,int? Winner,string Reason,int Plies,string? Error,long WhitePeakBytes,long BlackPeakBytes,List<object> Frames);
