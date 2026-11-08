using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Diagnostics;

// Offline fixed-depth comparison of our own frozen binaries. No game is played,
// no opponent is queried, no weights are written, no candidate is auto-selected.
if(args.Length is <4 or >6) throw new ArgumentException("Ametista.dll weights.json games.jsonl new-report.json [logicalProcessor] [depth]");
if(File.Exists(args[3])) throw new IOException("Do not overwrite a profile.");
if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("This experiment profile records Windows affinity.");
int logicalProcessor=args.Length>=5?int.Parse(args[4]):8;
int maximumDepth=args.Length>=6?int.Parse(args[5]):7;
if(maximumDepth is <1 or >16)throw new ArgumentOutOfRangeException(nameof(maximumDepth));
if(logicalProcessor<0||logicalProcessor>=Environment.ProcessorCount)throw new ArgumentOutOfRangeException(nameof(logicalProcessor));
Process.GetCurrentProcess().ProcessorAffinity=(nint)(1L<<logicalProcessor);
var binary=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(_,name)=>{
    var path=Path.Combine(Path.GetDirectoryName(binary)!,name.Name+".dll");
    return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
};
var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(binary);
var nt=assembly.GetType("Ametista.Network",true)!;
var bt=assembly.GetType("Ametista.Board",true)!;
var st=assembly.GetType("Ametista.Search",true)!;
var network=nt.GetMethod("Load")!.Invoke(null,[Path.GetFullPath(args[1])])!;
var search=Activator.CreateInstance(st,network)!;
var choose=st.GetMethod("Choose")!;
var samples=new List<object>();
int sample=0;
foreach(var line in File.ReadLines(args[2])) {
    using var game=JsonDocument.Parse(line);
    foreach(var frame in game.RootElement.GetProperty("frames").EnumerateArray()) {
        int ply=frame.GetProperty("ply").GetInt32();
        if(ply<6||ply%16!=0)continue;
        var cells=frame.GetProperty("board").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        uint white=0,black=0,kings=0;
        for(int s=0;s<32;s++) {if(cells[s]>0)white|=1u<<s;if(cells[s]<0)black|=1u<<s;if(Math.Abs(cells[s])==2)kings|=1u<<s;}
        // Synthetic probe from a recorded board: quiet/history reset identically
        // for both versions. This is not a reconstruction of the original game.
        var board=Activator.CreateInstance(bt,white,black,kings,frame.GetProperty("turn").GetInt32(),0)!;
        ulong key=(ulong)bt.GetProperty("Key")!.GetValue(board)!;
        if(sample==0) for(int warm=0;warm<3;warm++)
            choose.Invoke(search,[board,"warmup-"+warm,15000,new Dictionary<ulong,int>{{key,1}},CancellationToken.None,maximumDepth]);
        using var process=Process.GetCurrentProcess();
        var cpuStart=process.TotalProcessorTime;
        var wall=Stopwatch.StartNew();
        var result=choose.Invoke(search,[board,"profile-"+sample,15000,new Dictionary<ulong,int>{{key,1}},CancellationToken.None,maximumDepth])!;
        wall.Stop();var cpuMs=(process.TotalProcessorTime-cpuStart).TotalMilliseconds;
        var rt=result.GetType();
        int depth=(int)rt.GetProperty("Depth")!.GetValue(result)!;
        int score=(int)rt.GetProperty("Score")!.GetValue(result)!;
        if(depth!=0&&depth<maximumDepth&&Math.Abs(score)<29904)throw new InvalidOperationException("Timed out before fixed depth.");
        samples.Add(new {sample,sourceMatch=game.RootElement.GetProperty("matchId").GetString(),ply,
            position=new{white,black,kings,turn=frame.GetProperty("turn").GetInt32()},
            processCpuMilliseconds=cpuMs,wallMilliseconds=wall.Elapsed.TotalMilliseconds,
            result=JsonSerializer.SerializeToElement(result,rt)});
        if(++sample>=32)break;
    }
    if(sample>=32)break;
}
File.WriteAllText(args[3],JsonSerializer.Serialize(new {createdAt=DateTimeOffset.UtcNow,binary,
    searchVersion=st.GetField("Version")!.GetRawConstantValue(),depth=maximumDepth,logicalProcessor,warmupCalls=3,
    tieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")??"default",
    realGames=0,samples},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{sample} fixed-depth synthetic probes saved to {args[3]}");
