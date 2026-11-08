using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dama.Engine;
using Dama.Competition;

var root=Directory.GetCurrentDirectory();
if(!File.Exists(Path.Combine(root,"WeightSpaceGames.slnx")))throw new InvalidOperationException("Run from repository root.");
var input=Path.Combine(root,"models/Avventurina/results/conferma-a7");
var output=Path.Combine(root,"arena/ametista/2026-11-07/peer-audit-avventurina");
var registry=CompetitionStore.DefaultDirectory();
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
var errors=new List<string>();var warnings=new List<string>();var snapshots=new Dictionary<string,string>();
var allIds=new HashSet<string>();var reports=new List<BatchReport>();var allGames=new List<GameRecord>();
int moves=0;bool randomCompatible=true;
string Read(string path){var bytes=File.ReadAllBytes(path);snapshots[Path.GetRelativePath(root,path).Replace('\\','/')]=Convert.ToHexStringLower(SHA256.HashData(bytes));return File.ReadAllText(path);}
void Check(bool condition,string message){if(!condition)errors.Add(message);}
foreach(var directory in Directory.GetDirectories(input).Order())
{
    string batch=Path.GetFileName(directory);
    using var manifest=JsonDocument.Parse(Read(Path.Combine(directory,"manifest.json")));
    using var summary=JsonDocument.Parse(Read(Path.Combine(directory,"summary.json")));
    var m=manifest.RootElement;var s=summary.RootElement;
    var games=Read(Path.Combine(directory,"games.jsonl")).Split('\n',StringSplitOptions.RemoveEmptyEntries)
        .Select(line=>JsonSerializer.Deserialize<GameRecord>(line,json)!).ToArray();
    int seed=m.GetProperty("seed").GetInt32(),openingPlies=m.GetProperty("openingPlies").GetInt32();
    int declared=m.GetProperty("games").GetInt32(),thinkTime=m.GetProperty("thinkTimeMs").GetInt32();
    Check(games.Length==declared,$"{batch}: declared game count");
    Check(m.GetProperty("source").GetString()=="benchmark",$"{batch}: source");
    Check(Path.GetFullPath(m.GetProperty("competitionDirectory").GetString()!)==Path.GetFullPath(registry),$"{batch}: registry directory");
    Check(games.Select(g=>g.N).Order().SequenceEqual(Enumerable.Range(0,declared)),$"{batch}: game numbering");
    var avMs=new List<int>();var kingMs=new List<int>();var internalMs=new List<int>();
    int forcedMoves=0;var pairScores=new List<double>();
    foreach(var game in games)
    {
        allGames.Add(game);Check(Guid.TryParseExact(game.MatchId,"N",out _)&&allIds.Add(game.MatchId),$"{batch}: invalid/duplicate match ID {game.MatchId}");
        Check(new[]{game.White,game.Black}.Order().SequenceEqual(new[]{"avventurina","kingsrow"}),$"{game.MatchId}: players");
        Check(game.White==(game.N%2==0?"avventurina":"kingsrow"),$"{game.MatchId}: colors");
        Check(game.OpeningSeed==seed+game.N/2,$"{game.MatchId}: opening seed sequence");
        var p=Position.Initial();var seen=new Dictionary<string,int>{{p.Key,1}};var rng=new Random(game.OpeningSeed);
        Check(game.Frames.Length==game.Plies+1,$"{game.MatchId}: frame count");
        Check(game.Frames[0].Ply==0&&game.Frames[0].Turn==p.Turn&&game.Frames[0].Board.SequenceEqual(p.Board),$"{game.MatchId}: initial board");
        foreach(var frame in game.Frames.Skip(1))
        {
            var legal=Rules.LegalMoves(p);Check(Rules.GetOutcome(p,seen.GetValueOrDefault(p.Key),legal) is null,$"{game.MatchId}/{frame.Ply}: move after terminal");
            var move=legal.SingleOrDefault(x=>x.Id==frame.MoveId);
            if(move is null){errors.Add($"{game.MatchId}/{frame.Ply}: illegal move {frame.MoveId}");break;}
            if(frame.Ply<=openingPlies){Check(frame.By=="opening",$"{game.MatchId}/{frame.Ply}: opening attribution");randomCompatible &= legal[rng.Next(legal.Count)].Id==move.Id;}
            else
            {
                string actor=p.Turn==1?game.White:game.Black;
                Check(frame.By==actor,$"{game.MatchId}/{frame.Ply}: actor");
                Check(frame.Ms>=0,$"{game.MatchId}/{frame.Ply}: negative time");
                (actor=="avventurina"?avMs:kingMs).Add(frame.Ms);
                if(actor=="avventurina"&&frame.Comment is not null)
                {
                    var match=Regex.Match(frame.Comment,@", (\d+) ms(?: |$)");
                    if(match.Success)internalMs.Add(int.Parse(match.Groups[1].Value));
                    if(frame.Comment.Contains("forzata",StringComparison.Ordinal))forcedMoves++;
                }
            }
            p=Rules.Apply(p,move);seen[p.Key]=seen.GetValueOrDefault(p.Key)+1;moves++;
            Check(p.Ply==frame.Ply&&p.Turn==frame.Turn&&p.Board.SequenceEqual(frame.Board),$"{game.MatchId}/{frame.Ply}: state mismatch");
        }
        var outcome=Rules.GetOutcome(p,seen.GetValueOrDefault(p.Key));
        Check(game.Plies==p.Ply,$"{game.MatchId}: plies");
        Check(game.Completed==(outcome is not null),$"{game.MatchId}: completion");
        string? winner=outcome is null||outcome.Winner==0?null:outcome.Winner==1?game.White:game.Black;
        Check(game.Winner==winner,$"{game.MatchId}: winner");
        if(outcome is not null)Check(game.Reason==outcome.Reason&&game.Error is null,$"{game.MatchId}: reason/error");
        var recordPath=Path.Combine(registry,game.MatchId+".json");
        if(!File.Exists(recordPath)){errors.Add($"{game.MatchId}: missing registry record");continue;}
        var record=JsonSerializer.Deserialize<MatchRecord>(Read(recordPath),json)!;
        Check(record.Id==game.MatchId&&record.Source=="benchmark"&&record.Type=="ai-ai",$"{game.MatchId}: registry identity/source/type");
        Check(record.White?.Id=="ai:"+game.White&&record.Black?.Id=="ai:"+game.Black&&record.White.Kind=="ai"&&record.Black.Kind=="ai",$"{game.MatchId}: registry players");
        Check(record.Plies==game.Plies&&record.Winner==outcome?.Winner&&record.Status==(game.Completed?"finished":"interrupted")&&record.Reason==game.Reason,$"{game.MatchId}: registry result");
        Check(record.StartedAt>=m.GetProperty("startedAt").GetDateTimeOffset()&&record.UpdatedAt>=record.StartedAt,$"{game.MatchId}: timestamps");
    }
    foreach(var pair in games.GroupBy(g=>g.OpeningSeed))
    {
        var pairGames=pair.OrderBy(g=>g.N).ToArray();
        Check(pairGames.Length==2&&pairGames[0].White==pairGames[1].Black&&pairGames[0].Black==pairGames[1].White,$"{batch}/{pair.Key}: color pairing");
        if(pairGames.Length==2){Check(Prefix(pairGames[0],openingPlies)==Prefix(pairGames[1],openingPlies),$"{batch}/{pair.Key}: opening pairing");pairScores.Add(pairGames.Average(Score));}
    }
    int wins=games.Count(g=>g.Completed&&g.Winner=="avventurina"),draws=games.Count(g=>g.Completed&&g.Winner is null),losses=games.Count(g=>g.Completed&&g.Winner=="kingsrow"),interrupted=games.Count(g=>!g.Completed);
    double score=(wins+.5*draws)/games.Length;
    Check(s.GetProperty("wins").GetInt32()==wins&&s.GetProperty("draws").GetInt32()==draws&&s.GetProperty("losses").GetInt32()==losses&&s.GetProperty("interrupted").GetInt32()==interrupted&&Math.Abs(s.GetProperty("score").GetDouble()-score)<1e-12,$"{batch}: summary");
    reports.Add(new(batch,games.Length,wins,draws,losses,interrupted,score,m.GetProperty("avventurina").GetProperty("tablebasePieces").GetInt32(),thinkTime,Timing(avMs),Timing(kingMs),Timing(internalMs),forcedMoves,pairScores.ToArray()));
}
var tb=reports.Where(r=>r.TablebasePieces>0).ToArray();var noTb=reports.Where(r=>r.TablebasePieces==0).ToArray();
Check(tb.Sum(r=>r.Games)==36&&tb.Sum(r=>r.Wins)==2&&tb.Sum(r=>r.Draws)==11&&tb.Sum(r=>r.Losses)==23,"tablebase aggregate differs from 36: 2/11/23");
Check(noTb.Sum(r=>r.Games)==12&&noTb.Sum(r=>r.Wins)==2&&noTb.Sum(r=>r.Draws)==2&&noTb.Sum(r=>r.Losses)==8,"without-tablebase aggregate differs from 12: 2/2/8");
foreach(var left in allGames.Where(g=>g.OpeningSeed is >=7001 and <=7006).GroupBy(g=>g.N))
    Check(left.Select(g=>Prefix(g,6)).Distinct().Count()==1,$"same seed/color opening differs across tablebase and no-tablebase: {left.Key}");
foreach(var (path,hash) in snapshots)Check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,path))))==hash,$"Input changed during audit: {path}");
warnings.Add("Manifest declares HTTP thinkTimeMs=1100, not a measured equal 1000 ms search time. Avventurina comments self-report internal times; Kingsrow has no internal-time telemetry in these data. HTTP latencies include overhead and can exceed both thresholds.");
warnings.Add("CPU affinity and four concurrent pairs are manifest declarations only. Physical-core isolation, identical effective CPU budget, no pondering and absence of external load cannot be reconstructed from trajectories.");
warnings.Add("Manifest has a Kingsrow DLL hash but no Avventurina source/binary/weights/tablebase hash. This audit reads no decision code, weights or tablebases and cannot certify frozen implementation, tablebase provenance/completeness, or runtime linkage of any hash.");
warnings.Add("Final registry records and their timestamps are consistent, but alone cannot prove that every Start preceded the first move: records do not contain an append-only event history.");
warnings.Add("Three tablebase batches represent 18 opening pairs; no-tablebase batch represents six. The latter shares only the tb-7001 openings; its aggregate cannot isolate a tablebase effect against all 36 tablebase games.");
warnings.Add("A win proves that win under the recorded conditions, not superiority. Any inference should treat opening pair as unit and disclose development/selection history, which these files do not establish.");
var report=new {verifiedAt=DateTimeOffset.UtcNow,source="models/Avventurina/results/conferma-a7",passed=errors.Count==0,errors,warnings,games=allGames.Count,uniqueIds=allIds.Count,moves,registeredResultsMatch=errors.Count==0,openingPairs=24,distinctOpeningSeeds=allGames.Select(g=>g.OpeningSeed).Distinct().Count(),dotNetRandomPrefixesCompatible=randomCompatible,tablebase=Aggregate(tb),withoutTablebase=Aggregate(noTb),batches=reports,inputHashes=snapshots};
Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"audit.json"),JsonSerializer.Serialize(report,json));
Console.WriteLine(JsonSerializer.Serialize(new {report.passed,report.games,report.uniqueIds,report.moves,report.tablebase,report.withoutTablebase,report.errors,report.dotNetRandomPrefixesCompatible},json));
if(errors.Count!=0)Environment.ExitCode=1;

static string Prefix(GameRecord g,int plies)=>string.Join("|",g.Frames.Skip(1).Take(plies).Select(f=>f.MoveId));
static double Score(GameRecord g)=>!g.Completed?double.NaN:g.Winner=="avventurina"?1:g.Winner is null?.5:0;
static object Timing(List<int> values){var x=values.Order().ToArray();return new{count=x.Length,min=x.Length==0?0:x[0],median=x.Length==0?0:x[x.Length/2],max=x.Length==0?0:x[^1],over1000=x.Count(v=>v>1000),over1100=x.Count(v=>v>1100)};}
static object Aggregate(BatchReport[] batches){var scores=batches.SelectMany(b=>b.PairScores).ToArray();double mean=scores.Average(),sd=Math.Sqrt(scores.Sum(x=>(x-mean)*(x-mean))/(scores.Length-1)),margin=(scores.Length==18?2.109815578:2.570581836)*sd/Math.Sqrt(scores.Length);return new{games=batches.Sum(b=>b.Games),wins=batches.Sum(b=>b.Wins),draws=batches.Sum(b=>b.Draws),losses=batches.Sum(b=>b.Losses),interrupted=batches.Sum(b=>b.Interrupted),score=mean,openingPairs=scores.Length,approximatePairedT95=new[]{Math.Max(0,mean-margin),Math.Min(1,mean+margin)}};}
record Frame(int Ply,int[] Board,int Turn,string? MoveId,string? By,int Ms,string? Comment);
record GameRecord(string MatchId,int N,int OpeningSeed,string White,string Black,bool Completed,string? Winner,string Reason,int Plies,string? Error,Frame[] Frames);
record BatchReport(string Batch,int Games,int Wins,int Draws,int Losses,int Interrupted,double Score,int TablebasePieces,int HttpThinkTimeMs,object AvventurinaHttpMs,object KingsrowHttpMs,object AvventurinaSelfReportedMs,int ForcedAvventurinaMoves,double[] PairScores);
