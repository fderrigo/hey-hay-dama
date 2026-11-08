using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dama.Engine;

// Replay historical human records only. No CompetitionStore, engine calls or training.
if(args.Length == 1 && args[0] == "--verify") {
    Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks = Importer.Verify(), realGamesCreated = 0 }));
    return;
}
if(args.Length != 3) throw new ArgumentException("Usage: ci2004ita.zip ci2005ita.zip NEW_OUTPUT_DIRECTORY | --verify");
var output = Path.GetFullPath(args[2]);
if(Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must not exist.");
// Prevent accidental creation of dataset artifacts in the shared competition store.
if(output.Replace('\\','/').Split('/').Any(p => p.Equals("competition", StringComparison.OrdinalIgnoreCase)))
    throw new IOException("Output must be outside competition storage.");
int verifiedChecks = Importer.Verify();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var sources = new List<object>();
var reports = new List<object>();
var accepted = new List<Sample>();
var ids = new HashSet<string>(StringComparer.Ordinal);
int games = 0, acceptedGames = 0;
for(int index = 0; index < 2; index++) {
    int year = 2004 + index;
    byte[] zipBytes = File.ReadAllBytes(args[index]);
    using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
    if(archive.Entries.Count != 1 || !archive.Entries[0].FullName.Equals($"ci{year}ita.pdn", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Expected exactly the named PDN, not arbitrary archive contents.");
    var entry = archive.Entries[0];
    if(entry.Length > 2_000_000) throw new InvalidDataException("Unexpectedly large PDN.");
    using var source = entry.Open();
    using var buffer = new MemoryStream();
    source.CopyTo(buffer);
    byte[] pdnBytes = buffer.ToArray();
    string text, encoding;
    try { text = new UTF8Encoding(false, true).GetString(pdnBytes).TrimStart('\uFEFF'); encoding = "utf-8"; }
    catch(DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(pdnBytes); encoding = "windows-1252"; }
    text = text.Replace("\r\n", "\n").Replace('\r', '\n');
    var starts = Regex.Matches(text, "^[ \\t]*\\[(?:Event|Dati)\\s+", RegexOptions.Multiline);
    if(starts.Count == 0 || !string.IsNullOrWhiteSpace(text[..starts[0].Index]))
        throw new InvalidDataException("Missing first game header or unexpected archive prefix.");
    int sourceAccepted = 0, sourceSamples = 0;
    for(int g = 0; g < starts.Count; g++) {
        string block = text[starts[g].Index..(g+1 < starts.Count ? starts[g+1].Index : text.Length)].Trim();
        string id = Importer.Hash(Encoding.UTF8.GetBytes(year + "\n" + block));
        games++;
        try {
            if(!ids.Add(id)) throw new InvalidDataException("duplicate-game-content");
            var replay = Importer.Replay(block, year, id);
            accepted.AddRange(replay.Samples);
            acceptedGames++; sourceAccepted++; sourceSamples += replay.Samples.Count;
            reports.Add(new { gameId=id,sourceYear=year,sourceGameIndex=g+1,accepted=true,reason="legal-replay-and-consistent-result",plies=replay.Samples.Count,result=replay.Result,terminalByLocalRules=replay.Terminal });
        } catch(InvalidDataException error) {
            reports.Add(new {gameId=id,sourceYear=year,sourceGameIndex=g+1,accepted=false,reason=error.Message});
        }
    }
    sources.Add(new {sourceYear=year,url=$"https://www.fid.it/download/pdn/ci{year}ita.zip",zipSha256=Importer.Hash(zipBytes),pdnSha256=Importer.Hash(pdnBytes),pdnEntry=entry.FullName,encoding,games=starts.Count,acceptedGames=sourceAccepted,rejectedGames=starts.Count-sourceAccepted,positions=sourceSamples});
}
Directory.CreateDirectory(output);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using(var writer = new StreamWriter(Path.Combine(output,"positions.jsonl"),false,new UTF8Encoding(false)))
    foreach(var sample in accepted) writer.WriteLine(JsonSerializer.Serialize(sample,json));
using(var writer = new StreamWriter(Path.Combine(output,"games-audit.jsonl"),false,new UTF8Encoding(false)))
    foreach(var report in reports) writer.WriteLine(JsonSerializer.Serialize(report,json));
var summary = new {verifiedAt=DateTimeOffset.UtcNow,kind="historical-human-pdn-replay",realGamesCreated=0,training=false,
    games,acceptedGames,rejectedGames=games-acceptedGames,positions=accepted.Count,verifiedChecks,sources,
    positionsSha256=Importer.Hash(File.ReadAllBytes(Path.Combine(output,"positions.jsonl"))),
    gameAuditSha256=Importer.Hash(File.ReadAllBytes(Path.Combine(output,"games-audit.jsonl"))),
    conventions=new {board="32 integers: white man=1,king=2; black=-1,-2",turn="1 white, -1 black",result="published final outcome from white perspective: 1 white win, 0 draw, -1 black win",sample="position BEFORE human move; entire rejected games contribute zero samples",gameId="SHA256(year + newline + newline-normalized trimmed full PDN game block)",notation="PDN squares 1..32 converted to referee indices 0..31"},
    limitations=new[]{"Public download is not an identified open license; no claim of permission to redistribute the corpus.","Human results can reflect resignation or agreed draws; a nonterminal final board is allowed.","Ambiguous capture paths, unsupported tokens/setup, unknown or inconsistent results are rejected, never guessed.","No statistical independence claim for positions from the same game or repeated openings.","Replay uses current Dama.Engine rules, not an independently certified historical referee."}};
File.WriteAllText(Path.Combine(output,"audit.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions(json){WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(summary,json));

internal sealed record Sample(int[] Board,int Turn,string GameId,int Result,int SourceYear,int Ply);
internal sealed record ReplayResult(List<Sample> Samples,int Result,bool Terminal);

internal static class Importer {
    private static readonly Regex Header = new("\\G\\s*\\[([A-Za-z][A-Za-z0-9_]*)\\s+\"([^\"\\r\\n]*)\"\\s*\\]",RegexOptions.CultureInvariant);
    private static readonly Regex Number = new("\\G([0-9]+)(\\.\\.\\.|\\.)(?!\\.)",RegexOptions.CultureInvariant);
    private static readonly Regex MoveToken = new("\\G[0-9]{1,2}(?:[-xX][0-9]{1,2})+",RegexOptions.CultureInvariant);
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static int Result(string value) => value switch {"1-0"=>1,"0-1"=>-1,"1/2-1/2"=>0,_=>throw new InvalidDataException("unknown-result:"+value)};

    public static ReplayResult Replay(string block,int year,string id) {
        var tags = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        int cursor=0;
        while(true) {
            var match=Header.Match(block,cursor);
            if(!match.Success) break;
            string key=match.Groups[1].Value switch {"Dati"=>"Event","Anno"=>"Date","Bianco"=>"White","Nero"=>"Black","Risultato"=>"Result","Apertura"=>"ECO",var other=>other};
            if(!tags.TryAdd(key,match.Groups[2].Value)) throw new InvalidDataException("duplicate-tag:"+key);
            cursor=match.Index+match.Length;
        }
        if(!tags.ContainsKey("Event")||!tags.TryGetValue("Result",out string? label))throw new InvalidDataException("missing-event-or-result-tag");
        if(tags.ContainsKey("FEN")||tags.ContainsKey("SetUp"))throw new InvalidDataException("unsupported-setup");
        int result=Result(label);
        string body=RemoveComments(block[cursor..]);
        cursor=0;var position=Position.Initial();var samples=new List<Sample>();
        var repetitions=new Dictionary<string,int>{{position.Key,1}};
        string? movetextResult=null;
        while(cursor<body.Length) {
            if(char.IsWhiteSpace(body[cursor])) {cursor++;continue;}
            if(movetextResult is not null)throw new InvalidDataException("tokens-after-result");
            string? end=new[]{"1/2-1/2","1-0","0-1","*"}.FirstOrDefault(x=>body.AsSpan(cursor).StartsWith(x,StringComparison.Ordinal)
                &&(cursor+x.Length==body.Length||char.IsWhiteSpace(body[cursor+x.Length])));
            if(end is not null) {movetextResult=end;cursor+=end.Length;continue;}
            var number=Number.Match(body,cursor);
            if(number.Success) {
                if(!int.TryParse(number.Groups[1].Value,out int fullmove)||fullmove!=position.Ply/2+1
                    ||(number.Groups[2].Value=="..."?position.Turn!=-1:position.Turn!=1))throw new InvalidDataException("incorrect-move-number-at-ply:"+position.Ply);
                cursor+=number.Length;continue;
            }
            var token=MoveToken.Match(body,cursor);
            if(!token.Success || token.Index+token.Length<body.Length&&!char.IsWhiteSpace(body[token.Index+token.Length]))
                throw new InvalidDataException("unexpected-token-at-ply:"+position.Ply);
            if(Rules.GetOutcome(position,repetitions[position.Key]) is not null)throw new InvalidDataException("moves-after-local-terminal-at-ply:"+position.Ply);
            var move=SelectMove(token.Value,Rules.LegalMoves(position));
            samples.Add(new((int[])position.Board.Clone(),position.Turn,id,result,year,position.Ply));
            position=Rules.Apply(position,move);
            repetitions[position.Key]=repetitions.GetValueOrDefault(position.Key)+1;
            cursor+=token.Length;
        }
        if(movetextResult is null)throw new InvalidDataException("missing-movetext-result");
        if(Result(movetextResult)!=result)throw new InvalidDataException("header-movetext-result-mismatch");
        if(samples.Count==0)throw new InvalidDataException("no-moves");
        var outcome=Rules.GetOutcome(position,repetitions[position.Key]);
        if(outcome is not null && outcome.Winner!=result)throw new InvalidDataException("local-terminal-result-mismatch");
        return new(samples,result,outcome is not null);
    }

    internal static Move SelectMove(string token,List<Move> legal) {
        bool capture=token.Contains('x')||token.Contains('X');
        if(capture&&token.Contains('-'))throw new InvalidDataException("mixed-move-separators");
        int[] path=token.Split(['-','x','X']).Select(int.Parse).ToArray();
        if(path.Any(x=>x is <1 or >32))throw new InvalidDataException("square-out-of-range");
        path=path.Select(x=>x-1).ToArray();
        var candidates=legal.Where(m=>(m.Captures.Length>0)==capture && m.From==path[0] && m.To==path[^1]
            &&(capture&&path.Length==2||m.Path.SequenceEqual(path))).ToArray();
        if(candidates.Length==0)throw new InvalidDataException("illegal-move:"+token);
        if(candidates.Length!=1)throw new InvalidDataException("ambiguous-capture:"+token);
        return candidates[0];
    }

    private static string RemoveComments(string body) {
        var clean=new StringBuilder(body.Length);bool braces=false,line=false;
        foreach(char c in body) {
            if(line) {if(c=='\n'){line=false;clean.Append(' ');}continue;}
            if(braces) {if(c=='{')throw new InvalidDataException("nested-comment");if(c=='}'){braces=false;clean.Append(' ');}continue;}
            if(c=='{'){braces=true;clean.Append(' ');}
            else if(c==';'){line=true;clean.Append(' ');}
            else if(c=='}')throw new InvalidDataException("unmatched-comment");
            else clean.Append(c);
        }
        if(braces)throw new InvalidDataException("unterminated-comment");
        return clean.ToString();
    }

    public static int Verify() {
        int checks=0;
        void Check(bool condition){if(!condition)throw new InvalidDataException("Importer fixture failed");checks++;}
        void Reject(Action action,string reason){try{action();}catch(InvalidDataException e){Check(e.Message.StartsWith(reason,StringComparison.Ordinal));return;}throw new InvalidDataException("Invalid fixture was accepted");}
        var initial=Position.Initial();var move=SelectMove("21-17",Rules.LegalMoves(initial));var after=Rules.Apply(initial,move);
        Check(move.From==20&&move.To==16&&after.Board[20]==0&&after.Board[16]==1&&after.Turn==-1);
        var board=new int[32];board[21]=1;board[17]=-1;
        var capture=SelectMove("22x13",Rules.LegalMoves(new Position(board)));
        Check(capture.From==21&&capture.To==12&&capture.Captures.SequenceEqual(new[]{17}));
        Reject(()=>SelectMove("0-17",Rules.LegalMoves(initial)),"square-out-of-range");
        Reject(()=>SelectMove("21x17",Rules.LegalMoves(initial)),"illegal-move");
        Reject(()=>SelectMove("22x13",new List<Move>{capture,capture}),"ambiguous-capture");
        const string header="[Event \"synthetic\"]\n[Result \"1-0\"]\n";
        var valid=Replay(header+"1.21-17 {comment} ;comment\n 1-0",2004,"fixture");
        Check(valid.Samples.Count==1&&valid.Samples[0].Board.SequenceEqual(initial.Board)&&valid.Samples[0].Turn==1&&valid.Result==1);
        Check(Replay("[Dati \"synthetic\"]\n[Risultato \"1/2-1/2\"]\n1.21-17 1/2-1/2",2005,"fixture").Result==0);
        Reject(()=>Replay(header+"1.21-17 *",2004,"fixture"),"unknown-result");
        Reject(()=>Replay(header+"1.21-17 0-1",2004,"fixture"),"header-movetext-result-mismatch");
        Reject(()=>Replay(header+"1.21-17 # 1-0",2004,"fixture"),"unexpected-token");
        Reject(()=>Replay(header+"1.21-17 {oops 1-0",2004,"fixture"),"unterminated-comment");
        Reject(()=>Replay(header+"2.21-17 1-0",2004,"fixture"),"incorrect-move-number");
        return checks;
    }
}
