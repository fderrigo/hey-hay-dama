using System.Net.Http.Json;
using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

// Run against a temporary web host with its own room directory and the shared
// competition directory. Stop that host before Interrupt; never replay its rooms
// afterward: a later room update could restore the match's active status.
internal static class WebIntegration
{
    private const string Nickname="Ametista-Verify";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private sealed record Settings(string WebBaseUrl,string ModelBaseUrl,string ExpectedSearch,string ExpectedWeightsVersion,string ExpectedWeightsSha256,string ReportPath);
    public static async Task Check(string configuration)
    {
        var cfg=JsonSerializer.Deserialize<Settings>(File.ReadAllText(configuration),Json)!;
        if(!new Uri(cfg.WebBaseUrl).IsLoopback||!new Uri(cfg.ModelBaseUrl).IsLoopback)throw new InvalidOperationException("Use isolated local test hosts.");
        if(File.Exists(cfg.ReportPath))throw new IOException("Do not overwrite an integration check or reuse its match.");
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
        http.DefaultRequestHeaders.Add("X-Match-Source","test");
        var health=await http.GetFromJsonAsync<JsonElement>(cfg.ModelBaseUrl.TrimEnd('/')+"/health");
        if(health.GetProperty("search").GetString()!=cfg.ExpectedSearch||health.GetProperty("version").GetString()!=cfg.ExpectedWeightsVersion||health.GetProperty("sha256").GetString()!=cfg.ExpectedWeightsSha256)
            throw new InvalidDataException("Unexpected model for web integration.");
        var catalog=await http.GetFromJsonAsync<JsonElement>(cfg.WebBaseUrl.TrimEnd('/')+"/api/models");
        if(!catalog.GetProperty("models").EnumerateArray().Any(m=>m.GetProperty("id").GetString()=="ametista"))throw new InvalidDataException("Ametista missing from enabled catalog.");
        // Prepare storage before creating any real match. Keep a handle open so
        // later reports do not depend on reopening a possibly unwritable path.
        var store=new CompetitionStore(CompetitionStore.DefaultDirectory());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cfg.ReportPath))!);
        using var reportFile=new FileStream(cfg.ReportPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        var report=new Dictionary<string,object?>{{"kind","ametista-web-check"},{"createdAt",DateTimeOffset.UtcNow},{"realGamesCreated",0},{"modelHealth",health},{"competitionDirectory",store.DirectoryPath},{"passed",false}};
        void Write() {
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(report,Json);
            reportFile.Position=0;reportFile.Write(bytes);reportFile.SetLength(bytes.Length);reportFile.Flush(true);
        }
        Write();
        using var response=await http.PostAsJsonAsync(cfg.WebBaseUrl.TrimEnd('/')+"/api/games",new {white="ametista",black="human",access="online",humanSide="black",blackNickname=Nickname,source="test"});
        response.EnsureSuccessStatusCode();
        var created=await response.Content.ReadFromJsonAsync<JsonElement>();
        var game=created.GetProperty("game");string id=game.GetProperty("id").GetString()!;
        report["matchId"]=id;report["realGamesCreated"]=1;report["initialGame"]=game;
        Write();
        try {
            var initial=Read(store,id);ValidateOwned(initial);
            if(initial.Status!="active"||initial.Plies!=0)throw new InvalidDataException("Game was not recorded before the first move.");
            report["registeredBeforeMove"]=true;
            http.DefaultRequestHeaders.Add("X-Game-Token",created.GetProperty("token").GetString());
            using var advance=await http.PostAsJsonAsync(cfg.WebBaseUrl.TrimEnd('/')+"/api/games/"+id+"/advance",new {expectedPly=0});
            report["advanceHttpStatus"]=(int)advance.StatusCode;
            if(!advance.IsSuccessStatusCode) {
                report["advanceErrorBody"]=await advance.Content.ReadAsStringAsync();
                Write();
            }
            advance.EnsureSuccessStatusCode();
            var after=await advance.Content.ReadFromJsonAsync<JsonElement>();report["afterMove"]=after;
            var played=after.GetProperty("history").EnumerateArray().Single();
            var move=Rules.LegalMoves(Position.Initial()).Single(m=>m.Id==played.GetProperty("moveId").GetString());
            var expected=Rules.Apply(Position.Initial(),move);
            if(after.GetProperty("position").GetProperty("ply").GetInt32()!=1||after.GetProperty("position").GetProperty("turn").GetString()!="black"
                ||!after.GetProperty("position").GetProperty("board").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(expected.Board))
                throw new InvalidDataException("Web move differs from referee.");
            var recorded=Read(store,id);ValidateOwned(recorded);
            if(recorded.Status!="active"||recorded.Plies!=1||recorded.Winner is not null)throw new InvalidDataException("Web move not reflected in shared register.");
            report["passed"]=true;report["legalModelMove"]=true;Write();
            Console.WriteLine(JsonSerializer.Serialize(new {id,passed=true,plies=1,next="Stop temporary web host, then --interrupt-web-check with this report."}));
        } catch(Exception error) {report["error"]=error.Message;Write();throw;}
    }
    public static void Interrupt(string reportPath)
    {
        using var report=JsonDocument.Parse(File.ReadAllText(reportPath));
        if(report.RootElement.GetProperty("kind").GetString()!="ametista-web-check")throw new InvalidDataException("Not an owned integration report.");
        string id=report.RootElement.GetProperty("matchId").GetString()!;
        var store=new CompetitionStore(CompetitionStore.DefaultDirectory());var record=Read(store,id);ValidateOwned(record);
        if(record.Status!="active"||record.Winner is not null)throw new InvalidOperationException("Only the unfinished test can be interrupted.");
        store.Save(record with {Status="interrupted",Winner=null,Reason="integration-check-complete",UpdatedAt=DateTimeOffset.UtcNow});
        var final=Read(store,id);
        if(final.Status!="interrupted"||final.Winner is not null)throw new InvalidDataException("Interruption not recorded.");
        File.WriteAllText(reportPath+".interrupted.json",JsonSerializer.Serialize(final,Json));
        Console.WriteLine(JsonSerializer.Serialize(new {id,status=final.Status,points=0}));
    }
    private static MatchRecord Read(CompetitionStore store,string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Invalid test ID.");
        return JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(Path.Combine(store.DirectoryPath,id+".json")),Json)!;
    }
    private static void ValidateOwned(MatchRecord record)
    {
        if(record.Source!="test"||record.White?.Id!="ai:ametista"||record.Black?.Id!=Participant.Human(Nickname).Id)
            throw new InvalidDataException("Refusing to touch another match.");
    }
}
