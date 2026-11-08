extern alias baseline;
extern alias human;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BBoard=baseline::Ametista.Board;
using BNetwork=baseline::Ametista.Network;
using HBoard=human::Ametista.Board;
using HNetwork=human::Ametista.Network;
using HWeights=human::Ametista.Weights;
using HumanOutcome=human::Ametista.HumanOutcome;

if(args.Length!=4 || !int.TryParse(args[3],out int cpu) || cpu<0 || cpu>=IntPtr.Size*8)
    throw new ArgumentException("Usage: MANUAL_JSON HUMAN_JSON NEW_REPORT_JSON LOGICAL_CPU (run only after games finish)");
string manualPath=Path.GetFullPath(args[0]),humanPath=Path.GetFullPath(args[1]),outputPath=Path.GetFullPath(args[2]);
if(File.Exists(outputPath)||Directory.Exists(outputPath))throw new IOException("Refusing to overwrite output.");
string outputParent=Path.GetDirectoryName(outputPath)!;
if(!Directory.Exists(outputParent))throw new IOException("Create the output parent before running the probe.");
using var process=Process.GetCurrentProcess();
if(OperatingSystem.IsWindows()) {
    process.ProcessorAffinity=(nint)(1L<<cpu);
    process.PriorityClass=ProcessPriorityClass.BelowNormal;
    if(process.ProcessorAffinity!=(nint)(1L<<cpu))throw new InvalidOperationException("Requested CPU affinity was not applied.");
} else throw new PlatformNotSupportedException("This diagnostic uses Windows process affinity.");

var repository=new DirectoryInfo(AppContext.BaseDirectory);
while(repository is not null&&!File.Exists(Path.Combine(repository.FullName,"WeightSpaceGames.slnx")))repository=repository.Parent;
if(repository is null)throw new IOException("Repository root required to check frozen DLLs.");
string originalBaseline=Path.Combine(repository.FullName,"artifacts","ametista-final-publish","Ametista.dll");
string originalHuman=Path.Combine(repository.FullName,"artifacts","ametista-human-publish","Ametista.HumanSearch.dll");
string loadedBaseline=typeof(BNetwork).Assembly.Location,loadedHuman=typeof(HNetwork).Assembly.Location;
var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
string Fingerprint(string path) {
    string hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    hashes[path]=hash;return hash;
}
if(Fingerprint(originalBaseline)!=Fingerprint(loadedBaseline)||Fingerprint(originalHuman)!=Fingerprint(loadedHuman))
    throw new InvalidDataException("Loaded inference DLL differs from frozen/published source artifact.");
string manualHash=Fingerprint(manualPath),humanHash=Fingerprint(humanPath);
if(manualHash!="7e8ea1c6a7c897e5d3d5c42a43511a26ad0ebd32db904fff670be488c7913138")
    throw new InvalidDataException("Expected frozen manual-v4 weights.");
string engineDll=Path.Combine(AppContext.BaseDirectory,"Dama.Engine.dll");
if(File.Exists(engineDll))Fingerprint(engineDll);
var baseNetwork=BNetwork.Load(manualPath);
var humanModel=HumanOutcome.Load(humanPath);
if(humanModel.Sha256!=humanHash || HumanOutcome.ContributionScale!=100)throw new InvalidDataException("Human model or fixed scale mismatch.");
var definition=JsonSerializer.Deserialize<HWeights>(File.ReadAllBytes(manualPath),HNetwork.JsonOptions)
    ?? throw new InvalidDataException("Missing manual definition.");
var combinedNetwork=new HNetwork(definition,humanModel);

const int seed=271828,boardCount=1000,roundCount=7,passes=200,warmupPasses=20;
var random=new Random(seed);
var boards=new BBoard[boardCount];var combinedBoards=new HBoard[boardCount];
for(int i=0;i<boardCount;i++) {
    var squares=Enumerable.Range(0,32).ToArray();
    for(int j=squares.Length-1;j>0;j--){int k=random.Next(j+1);(squares[j],squares[k])=(squares[k],squares[j]);}
    int whites=random.Next(1,13),blacks=random.Next(1,13);uint white=0,black=0,kings=0;
    for(int j=0;j<whites+blacks;j++) {
        int square=squares[j];uint bit=1u<<square;bool isWhite=j<whites;
        if(isWhite)white|=bit;else black|=bit;
        if(random.Next(3)==0||(isWhite?square<4:square>=28))kings|=bit;
    }
    int turn=i%2==0?1:-1;
    boards[i]=new(white,black,kings,turn,0);combinedBoards[i]=new(white,black,kings,turn,0);
}
string boardsHash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(boards.Select(b=>new {b.White,b.Black,b.Kings,b.Turn,b.Quiet}))));
int checks=0;
for(int i=0;i<boardCount;i++) {
    var b=baseNetwork.Evaluate(boards[i]);var h=combinedNetwork.Evaluate(combinedBoards[i]);
    int expected=b.Value+(int)Math.Round(100*humanModel.Predict(combinedBoards[i]));
    if(h.Value!=expected||h.Risk!=b.Risk)throw new InvalidDataException($"Actual DLL combination mismatch at board {i}.");
    checks++;
}
long EvaluateBaseline(int repeat) {
    long checksum=0;
    for(int p=0;p<repeat;p++)for(int i=0;i<boardCount;i++) {
        var estimate=baseNetwork.Evaluate(boards[i]);checksum+=estimate.Value*31L+estimate.Risk;
    }
    return checksum;
}
long EvaluateCombined(int repeat) {
    long checksum=0;
    for(int p=0;p<repeat;p++)for(int i=0;i<boardCount;i++) {
        var estimate=combinedNetwork.Evaluate(combinedBoards[i]);checksum+=estimate.Value*31L+estimate.Risk;
    }
    return checksum;
}
long warmupBaseline=EvaluateBaseline(warmupPasses),warmupCombined=EvaluateCombined(warmupPasses);
Measurement Measure(Func<int,long> evaluation) {
    long start=Stopwatch.GetTimestamp();long checksum=evaluation(passes);long elapsed=Stopwatch.GetTimestamp()-start;
    return new(elapsed,elapsed*1e9/Stopwatch.Frequency/(passes*boardCount),checksum);
}
var rounds=new List<Round>();
for(int round=0;round<roundCount;round++) {
    Measurement baselineTime,combinedTime;
    if(round%2==0) {baselineTime=Measure(EvaluateBaseline);combinedTime=Measure(EvaluateCombined);}
    else {combinedTime=Measure(EvaluateCombined);baselineTime=Measure(EvaluateBaseline);}
    if(baselineTime.Checksum!=warmupBaseline*(passes/warmupPasses)||combinedTime.Checksum!=warmupCombined*(passes/warmupPasses))
        throw new InvalidDataException("Inference checksum changed between warmup and measurement.");
    rounds.Add(new(round+1,round%2==0?"baseline-first":"combined-first",baselineTime,combinedTime,combinedTime.NanosecondsPerEvaluation/baselineTime.NanosecondsPerEvaluation));
}
double Median(IEnumerable<double> values){var a=values.Order().ToArray();return a[a.Length/2];}
double baselineMedian=Median(rounds.Select(r=>r.Baseline.NanosecondsPerEvaluation));
double combinedMedian=Median(rounds.Select(r=>r.Combined.NanosecondsPerEvaluation));
foreach(var (path,hash) in hashes)
    if(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))!=hash)throw new InvalidDataException("Input DLL/model changed during measurement.");
var report=new {measuredAt=DateTimeOffset.UtcNow,kind="actual-dll-static-inference-cost",SyntheticNotGameWeighted=true,
    playingStrengthMeasured=false,causalGameOutcomeClaim=false,realGamesCreated=0,training=false,logicalProcessor=cpu,
    seed,boardCount,boardsSha256=boardsHash,roundCount,passes,evaluationsPerRound=passes*boardCount,warmupPasses,
    numericalCombinationChecks=checks,manualRiskUnchanged=true,contributionScale=100,baselineCompiledInference=baseNetwork.CompiledInference,
    combinedCompiledInference=combinedNetwork.CompiledInference,baselineMedianNanosecondsPerEvaluation=baselineMedian,
    combinedMedianNanosecondsPerEvaluation=combinedMedian,ratioOfMedians=combinedMedian/baselineMedian,
    medianRoundRatio=Median(rounds.Select(r=>r.CombinedOverBaseline)),rounds,inputSha256=hashes,
    runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,stopwatchFrequency=Stopwatch.Frequency,
    limitations=new[]{"Fixed synthetic boards, not reachable-game distribution or game-weighted evaluations.",
        "Measures static network evaluation without search, evaluation cache, move generation or HTTP.",
        "DLL identity checked by byte hashes; this is not historical runtime provenance proof.",
        "Alternating order and warmup reduce but do not eliminate JIT, scheduling or shared-load effects.",
        "Timing ratio does not explain or establish a cause of game outcomes."}};
using(var stream=new FileStream(outputPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
    JsonSerializer.Serialize(stream,report,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});
Console.WriteLine(JsonSerializer.Serialize(new {outputPath,checks,baselineMedian,combinedMedian,ratio=combinedMedian/baselineMedian,SyntheticNotGameWeighted=true,realGamesCreated=0}));

internal sealed record Measurement(long ElapsedStopwatchTicks,double NanosecondsPerEvaluation,long Checksum);
internal sealed record Round(int Number,string Order,Measurement Baseline,Measurement Combined,double CombinedOverBaseline);
