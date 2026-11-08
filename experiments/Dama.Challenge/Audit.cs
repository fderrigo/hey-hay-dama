using System.Text.Json;
using Dama.Engine;
using Dama.Competition;

static class ChallengeAudit
{
    public static void Run(string directory)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));
        var config=manifest.RootElement.GetProperty("config").Deserialize<Configuration>(json)!;
        using var input=new FileStream(Path.Combine(directory,"games.jsonl"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        using var reader=new StreamReader(input);
        var text=reader.ReadToEnd();
        // A running writer may have an unfinished last line. Audit only flushed records.
        int newline=text.LastIndexOf('\n');
        var games=(newline<0?"":text[..newline]).Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>JsonDocument.Parse(s)).ToArray();
        var ids=new HashSet<string>(); var openings=new Dictionary<(int,bool),string>();
        var pairScores=new Dictionary<int,List<double>>(); int moves=0, completed=0;
        var store=CompetitionStore.DefaultDirectory();
        foreach(var doc in games) {
            var g=doc.RootElement; var id=g.GetProperty("matchId").GetString()!;
            Require(ids.Add(id),"Duplicate match ID");
            var record=JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(Path.Combine(store,id+".json")),json)!;
            var white=g.GetProperty("white").GetString()!; var black=g.GetProperty("black").GetString()!;
            Require(record.Source==config.Source&&record.Type=="ai-ai"&&record.White!.Id=="ai:"+white&&record.Black!.Id=="ai:"+black,"Registry participants/source mismatch");
            int seed=g.GetProperty("openingSeed").GetInt32(); var random=new Random(seed);
            Require(config.Seeds.Contains(seed),"Unexpected opening seed");
            Require((white==config.A.Id&&black==config.B.Id)||(white==config.B.Id&&black==config.A.Id),"Unexpected opponents");
            var p=Position.Initial(); var seen=new Dictionary<string,int>{{p.Key,1}};
            var prefix=new List<string>();
            var frames=g.GetProperty("frames").EnumerateArray().ToArray();
            Require(frames[0].GetProperty("board").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(p.Board),"Initial board mismatch");
            foreach(var f in frames.Skip(1)) {
                var legal=Rules.LegalMoves(p);
                Require(Rules.GetOutcome(p,seen[p.Key],legal) is null,"Move after terminal result");
                string moveId=f.GetProperty("moveId").GetString()!;
                if(p.Ply<6) {Require(legal[random.Next(legal.Count)].Id==moveId,"Opening seed mismatch");prefix.Add(moveId);}
                var move=legal.Single(m=>m.Id==moveId);p=Rules.Apply(p,move);seen[p.Key]=seen.GetValueOrDefault(p.Key)+1;
                Require(p.Board.SequenceEqual(f.GetProperty("board").EnumerateArray().Select(x=>x.GetInt32()))&&p.Turn==f.GetProperty("turn").GetInt32()&&p.Ply==f.GetProperty("ply").GetInt32(),"Frame mismatch");
                moves++;
            }
            var result=Rules.GetOutcome(p,seen[p.Key]);
            Require(g.GetProperty("completed").GetBoolean()==(result is not null),"Completion mismatch");
            Require(record.Plies==p.Ply&&g.GetProperty("plies").GetInt32()==p.Ply,"Ply count mismatch");
            if(result is not null) {
                Require(record.Status=="finished"&&record.Winner==result.Winner&&record.Reason==result.Reason&&g.GetProperty("winner").GetInt32()==result.Winner&&g.GetProperty("reason").GetString()==result.Reason,"Result mismatch");completed++;
                if(!pairScores.TryGetValue(seed,out var scores)) pairScores[seed]=scores=[];
                scores.Add(result.Winner==0?.5:(result.Winner==1?white:black)==config.A.Id?1:0);
            } else Require(record.Status=="interrupted"&&record.Winner is null,"Interrupted match has a result");
            openings.Add((seed,white==config.A.Id),string.Join(' ',prefix));
        }
        bool full=games.Length==config.Seeds.Length*2;
        foreach(var seed in config.Seeds) if(openings.ContainsKey((seed,true))&&openings.ContainsKey((seed,false)))
            Require(openings[(seed,true)]==openings[(seed,false)],"Unpaired opening"); else full=false;
        var pairs=pairScores.Values.Where(x=>x.Count==2).Select(x=>x.Average()).ToArray();
        double? mean=pairs.Length==0?null:pairs.Average(), lower=null, upper=null;
        if(pairs.Length>=18) {
            var variance=pairs.Sum(x=>Math.Pow(x-mean!.Value,2))/(pairs.Length-1);
            var margin=2.11*Math.Sqrt(variance/pairs.Length); // conservative t approximation for >=18 pairs
            lower=Math.Max(0,mean!.Value-margin);upper=Math.Min(1,mean.Value+margin);
        }
        // Pair scores lie in [0,1]. Unlike the sample-variance t interval,
        // this bound remains nonzero when every observed pair is identical.
        // Its probability interpretation assumes independent sampled openings.
        double? boundedLower=null,boundedUpper=null;
        if(pairs.Length>=18) {
            var margin=Math.Sqrt(Math.Log(40)/(2*pairs.Length));
            boundedLower=Math.Max(0,mean!.Value-margin);boundedUpper=Math.Min(1,mean.Value+margin);
        }
        var report=new {verifiedAt=DateTimeOffset.UtcNow,games=games.Length,completed,moves,allRecorded=true,legalReplay=true,fullSchedule=full,
            pairedOpenings=openings.Keys.All(k=>openings.ContainsKey((k.Item1,!k.Item2))),availablePairsVerified=true,uniqueOpeningLines=openings.Values.Distinct().Count(),completePairs=pairs.Length,pairedScore=mean,
            approximate95Lower=lower,approximate95Upper=upper,intervalMethod="t approximation by opening pair; not reported below 18 complete pairs",
            bounded95Lower=boundedLower,bounded95Upper=boundedUpper,boundedIntervalMethod="two-sided Hoeffding on independent opening-pair scores in [0,1]; inference conditional on opening sampling",
            superioritySupported=full&&completed==games.Length&&lower>.5&&boundedLower>.5};
        File.WriteAllText(Path.Combine(directory,"audit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions(json){WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));foreach(var doc in games)doc.Dispose();
    }
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
}
