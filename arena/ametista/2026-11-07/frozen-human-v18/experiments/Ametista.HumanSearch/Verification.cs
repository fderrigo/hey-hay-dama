using System.Text.Json;
using Dama.Engine;

namespace Ametista;

public static class Verification
{
    public static void Run(Network selected,string manualSha256,string humanSha256)
    {
        var network=selected;
        Network? preservedGraph=null,previousGraph=null;
        int pressureCases=0;
        if(network.Definition.Version=="manual-v4") {
            preservedGraph=new Network(network.Definition with {Nodes=network.Definition.Nodes[..^4]});
            previousGraph=new Network(Network.ManualV3());
            var far=new Board(3u,1u<<31,3u|(1u<<31),1,0);
            var near=new Board((1u<<20)|(1u<<21),1u<<31,(1u<<20)|(1u<<21)|(1u<<31),1,0);
            int Bonus(Board b)=>network.Evaluate(b).Value-previousGraph.Evaluate(b).Value;
            if(Bonus(near)<=Bonus(far)||Bonus(near)<=0||Bonus(near)>=100)
                throw new InvalidDataException("King-majority pressure must favor coordinated proximity with a bounded bonus.");
            pressureCases++;
            var equal=near with {Black=(1u<<30)|(1u<<31),Kings=near.Kings|(1u<<30)};
            if(Bonus(equal)!=0)throw new InvalidDataException("Pressure must be gated off with equal king counts.");
            pressureCases++;
            if(Bonus(near with {Turn=-1})!=-Bonus(near))throw new InvalidDataException("Pressure perspective mismatch.");
            pressureCases++;
        }
        int exclusiveKingCases=0;
        if(network.Definition.Version=="manual-v3") {
            var baseline=new Network(Network.ManualV2());
            foreach(var (board,expectedBonus) in new (Board,int)[]{
                (new(1u<<4,(1u<<25)|(1u<<27),1u<<4,1,0),75),
                (new((1u<<4)|(1u<<5),(1u<<25)|(1u<<27),(1u<<4)|(1u<<5),1,0),150),
                (new(1u<<4,1u<<27,(1u<<4)|(1u<<27),1,0),0),
                (new((1u<<4)|(1u<<5),1u<<27,1u<<27,1,0),-75)}) {
                if(network.Evaluate(board).Value-baseline.Evaluate(board).Value!=expectedBonus)
                    throw new InvalidDataException("Exclusive king gate boundary mismatch.");
                exclusiveKingCases++;
            }
        }
        var buffer=new FastMove[MoveGenerator.Capacity];
        int positions=0, transitions=0;
        void Check(Position p)
        {
            var board=Board.From(p);
            if(preservedGraph is not null&&preservedGraph.Evaluate(board)!=previousGraph!.Evaluate(board))
                throw new InvalidDataException("Input expansion changed the preserved manual-v3 graph.");
            if(network.Evaluate(board)!=network.Evaluate(board,interpreted:true))
                throw new InvalidDataException("Compiled/interpreted inference mismatch.");
            if(network.Evaluate(board)!=network.Evaluate(board with {Quiet=board.Quiet==0?79:0}))
                throw new InvalidDataException("Static evaluation depends on quiet count: cache key must include it.");
            if(network.Evaluate(board)!=network.Evaluate(board,true)) throw new InvalidDataException("Vector/scalar neural encoder mismatch.");
            if(!board.ToPosition().Board.SequenceEqual(p.Board)) throw new InvalidDataException("Encoding mismatch.");
            var legal=Rules.LegalMoves(p);
            if(MoveGenerator.HasCapture(board)!=(legal.Count>0&&legal[0].Captures.Length>0)) throw new InvalidDataException("Capture existence mismatch.");
            if(legal.Count==0&&MoveGenerator.HasQuiet(board)) throw new InvalidDataException("Quiet terminal mismatch.");
            var expected=legal.Select(m=>Board.From(Rules.Apply(p,m))).ToHashSet();
            int count=MoveGenerator.Generate(board,buffer);
            var actual=buffer.Take(count).Select(board.Apply).ToHashSet();
            if(!expected.SetEquals(actual)) throw new InvalidDataException($"Move mismatch at {p.Key}: referee {expected.Count}, fast {actual.Count}");
            var rotated=new Position(p.Board.Reverse().Select(x=>-x).ToArray(),-p.Turn,0,p.QuietKingPlyCount);
            if(network.Evaluate(board)!=network.Evaluate(Board.From(rotated))) throw new InvalidDataException("Network rotation symmetry mismatch.");
            positions++; transitions+=count;
        }
        Check(Position.Initial()); Check(Position.Initial() with {Turn=-1});
        for(int s=0;s<32;s++) for(int d=0;d<4;d++) {
            var to=Geometry.Step[s,d];
            if(Geometry.Shift(1u<<s,d)!=(to<0?0:1u<<to)) throw new InvalidDataException("Bit shift mismatch.");
        }
        var random=new Random(834719);
        var searchFixtures=new List<Position>();
        for(int i=0;i<6000;i++) {
            var array=new int[32]; var squares=Enumerable.Range(0,32).OrderBy(_=>random.Next()).ToArray();
            int a=random.Next(1,13),b=random.Next(1,13);
            for(int j=0;j<a+b;j++) {
                int side=j<a?1:-1,s=squares[j];
                bool king=random.Next(3)==0||(side==1?s<4:s>=28);
                array[s]=side*(king?2:1);
            }
            var fixture=new Position(array,i%2==0?1:-1,0,random.Next(80));
            Check(fixture);
            if(searchFixtures.Count<32&&Rules.LegalMoves(fixture).Count>1&&a+b<=10) searchFixtures.Add(fixture);
        }
        var path="arena/kingsrow/2026-11-07/games.jsonl";
        if(!File.Exists(path))throw new InvalidDataException("Required existing replay fixture archive is missing.");
        foreach(var line in File.ReadLines(path)) {
            using var doc=JsonDocument.Parse(line);
            foreach(var frame in doc.RootElement.GetProperty("frames").EnumerateArray())
                Check(new Position(frame.GetProperty("board").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),frame.GetProperty("turn").GetInt32()));
        }
        var engine=new Search(network);
        long previousTurnCacheHits=0;
        foreach(var fixture in searchFixtures) {
            var b=Board.From(fixture);
            var result=engine.Choose(b,"reference-"+fixture.Key,5000,new Dictionary<ulong,int>{{b.Key,1}},maximumDepth:3);
            var seen=new Dictionary<string,int>{{fixture.Key,1}};
            int expected=Reference(fixture,3,0,seen,network);
            if(result.Score!=expected) throw new InvalidDataException($"PVS/reference mismatch: {fixture.Key}, fast {result.Score}, exact {expected}, depth {result.Depth}");
            var reused=engine.Choose(b,"reference-"+fixture.Key,5000,new Dictionary<ulong,int>{{b.Key,1}},maximumDepth:3);
            if(reused.Score!=expected)throw new InvalidDataException("Cached search differs from independent reference.");
            previousTurnCacheHits+=reused.PreviousTurnCacheHits;
        }
        if(previousTurnCacheHits==0)throw new InvalidDataException("No cross-call cache reuse exercised.");
        int aspirationReferenceCases=0;
        foreach(var fixture in searchFixtures.Take(12)) {
            var board=Board.From(fixture);
            var result=engine.Choose(board,"aspiration-"+fixture.Key,5000,new Dictionary<ulong,int>{{board.Key,1}},maximumDepth:4);
            int expected=Reference(fixture,4,0,new Dictionary<string,int>{{fixture.Key,1}},network);
            if(result.Score!=expected)throw new InvalidDataException("Aspiration/reference mismatch.");
            aspirationReferenceCases++;
        }
        int reductionCases=0;long verifiedReductions=0,verifiedReductionResearches=0;
        // Boards observed in development-search-v3. Reset history/quiet identically
        // for synthetic probes; no opponent scores or moves supervise the network.
        foreach(var board in new Board[]{
            new(3824680960u,124094u,0,1,0),
            new(629145604u,134234394u,134217732u,1,0),
            new(16814080u,536887304u,536891392u,1,0),
            new(18432u,2105472u,2123776u,1,0),
            new(134217792u,393344u,134611008u,1,0)}) {
            var result=engine.Choose(board,"lmr-fixture-"+reductionCases,10000,new Dictionary<ulong,int>{{board.Key,1}},maximumDepth:6);
            var position=board.ToPosition();
            int expected=Reference(position,6,0,new Dictionary<string,int>{{position.Key,1}},network);
            if(result.Depth!=6||result.Score!=expected)throw new InvalidDataException($"LMR/reference mismatch: {position.Key}, selective {result.Score}, exact {expected}, depth {result.Depth}");
            verifiedReductions+=result.LateReductions;verifiedReductionResearches+=result.ReductionResearches;reductionCases++;
        }
        if(verifiedReductions==0||verifiedReductionResearches==0)throw new InvalidDataException("LMR reduction and recovery paths must both be exercised.");
        var initial=Board.From(Position.Initial());
        var corner=new Board(1u<<4,1u<<27,(1u<<4)|(1u<<27),1,2);
        var cornerResult=engine.Choose(corner,"synthetic-zugzwang",500,new Dictionary<ulong,int>{{corner.Key,1}});
        var cornerPosition=corner.ToPosition();
        int cornerReference=Reference(cornerPosition,8,0,new Dictionary<string,int>{{cornerPosition.Key,1}},network);
        if(cornerResult.Score!=cornerReference||cornerReference!=29991)throw new InvalidDataException("Zugzwang probe not independently confirmed.");
        if(cornerResult.AspirationRetries==0)throw new InvalidDataException("Aspiration expansion not exercised.");
        int historyCases=0;
        foreach(var move in Rules.LegalMoves(cornerPosition)) {
            var future=Rules.Apply(cornerPosition,move);
            var remembered=new Dictionary<ulong,int>{{corner.Key,1},{Board.From(future).Key,3}};
            var repeated=engine.Choose(corner,"synthetic-zugzwang",5000,remembered,maximumDepth:8);
            int expected=Reference(cornerPosition,8,0,new Dictionary<string,int>{{cornerPosition.Key,1},{future.Key,3}},network);
            if(repeated.Score!=expected)throw new InvalidDataException("History-dependent cache differs from independent reference.");
            historyCases++;
        }
        // Exercise the actual storage boundary without constructing an artificial
        // 95-ply game. These are isolated rule fixtures, never competition games.
        int horizonCases=0;
        var boundary=typeof(Search).GetMethod("Negamax",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        foreach(var (fixture,repetition) in new (Board,int)[]{
            (new(0,1u<<27,0,1,0),1),
            (new(1u<<0,1u<<31,(1u<<0)|(1u<<31),1,80),1),
            (new(1u<<4,1u<<27,(1u<<4)|(1u<<27),1,0),4),
            (new(0,1u<<27,0,1,80),4)}) {
            engine.Choose(initial,"horizon-"+horizonCases,10,new Dictionary<ulong,int>{{initial.Key,1},{fixture.Key,repetition}},maximumDepth:1);
            var outcome=Rules.GetOutcome(fixture.ToPosition(),repetition);
            if(outcome is null)throw new InvalidDataException("Horizon fixture must be terminal.");
            int expected=outcome.Winner==0?0:-30000+95;
            int actual=(int)boundary.Invoke(engine,[fixture,0,-30000,30000,95,true])!;
            if(actual!=expected)throw new InvalidDataException("Storage horizon hides a referee outcome.");
            horizonCases++;
        }
        var decision=engine.Choose(initial,"synthetic-initial",200,new Dictionary<ulong,int>{{initial.Key,1}});
        if(!Rules.LegalMoves(Position.Initial()).Any(m=>Board.From(Rules.Apply(Position.Initial(),m))==initial.Apply(decision.Move))) throw new InvalidDataException("Illegal search result.");
        if(decision.Milliseconds>350) throw new InvalidDataException("Search exceeded timing tolerance.");
        var report=new {manualSha256,humanSha256,humanContributionScale=HumanOutcome.ContributionScale,verificationMetadataOnlyVersion=true,sourceManualVersion="manual-v4",weightsVersion=network.Definition.Version,compiledInference=network.CompiledInference,compiledInterpretedAgreement=true,exclusiveKingCases,pressureCases,preservedGraphAgreement=preservedGraph is not null,positions,transitions,legalTransitions=true,networkSymmetry=true,evaluationQuietInvariant=true,searchReferencePositions=searchFixtures.Count,aspirationReferenceCases,previousTurnCacheHits,historyCases,horizonCases,reductionCases,verifiedReductions,verifiedReductionResearches,search=decision,cornerProbe=cornerResult,realGames=0};
        const string outputDirectory="artifacts/ametista-human-search-verification";
        Directory.CreateDirectory(outputDirectory);
        string stamp=DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ");
        string outputPath=Path.Combine(outputDirectory,$"verification-{Search.Version}-{stamp}.json");
        using(var output=new FileStream(outputPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            JsonSerializer.Serialize(output,report,Network.JsonOptions);
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    private static int Reference(Position p,int depth,int ply,Dictionary<string,int> seen,Network network)
    {
        var legal=Rules.LegalMoves(p);var outcome=Rules.GetOutcome(p,seen.GetValueOrDefault(p.Key),legal);
        if(outcome is not null)return outcome.Winner==0?0:-30000+ply;
        if(depth<=0&&legal[0].Captures.Length==0)return network.Evaluate(Board.From(p)).Value;
        bool forcedCapture=legal[0].Captures.Length>0&&legal.Select(m=>Rules.Apply(p,m).Key).Distinct().Count()==1;
        int best=-30000;
        foreach(var move in legal) {
            var next=Rules.Apply(p,move);seen[next.Key]=seen.GetValueOrDefault(next.Key)+1;
            best=Math.Max(best,-Reference(next,forcedCapture?Math.Max(depth,0):Math.Max(depth,0)-1,ply+1,seen,network));
            if(--seen[next.Key]==0)seen.Remove(next.Key);
        }
        return best;
    }
}
