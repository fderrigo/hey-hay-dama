using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

static class Audit
{
    public static void Run(string directory)
    {
        var registry = CompetitionStore.DefaultDirectory();
        var games = File.ReadLines(Path.Combine(directory,"games.jsonl")).Select(x => JsonDocument.Parse(x)).ToArray();
        var ids = new HashSet<string>();
        var openings = new Dictionary<(string,int),string>();
        var moves = 0;
        foreach(var document in games)
        {
            var game = document.RootElement;
            var id = game.GetProperty("matchId").GetString()!;
            Require(ids.Add(id),"ID duplicato");
            var record = JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(Path.Combine(registry,id+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Require(record.Source == "benchmark" && record.Type == "ai-ai", "Origine o tipo errato");
            Require(record.White!.Id == "ai:"+game.GetProperty("white").GetString() && record.Black!.Id == "ai:"+game.GetProperty("black").GetString(), "Partecipanti discordanti");
            var p = Position.Initial();
            var seen = new Dictionary<string,int> { [p.Key] = 1 };
            var frames = game.GetProperty("frames").EnumerateArray().ToArray();
            Require(frames[0].GetProperty("board").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(p.Board),"Posizione iniziale errata");
            var opening = new List<string>();
            foreach(var frame in frames.Skip(1))
            {
                var legal = Rules.LegalMoves(p);
                Require(Rules.GetOutcome(p,seen[p.Key],legal) is null,"Mossa dopo fine partita");
                var move = legal.Single(m => m.Id == frame.GetProperty("moveId").GetString());
                p = Rules.Apply(p,move); seen[p.Key] = seen.GetValueOrDefault(p.Key)+1;
                Require(p.Board.SequenceEqual(frame.GetProperty("board").EnumerateArray().Select(x=>x.GetInt32())) && p.Ply == frame.GetProperty("ply").GetInt32() && p.Turn == frame.GetProperty("turn").GetInt32(),"Stato discordante");
                if(p.Ply<=6) opening.Add(move.Id);
                moves++;
            }
            var outcome = Rules.GetOutcome(p,seen[p.Key]);
            Require(game.GetProperty("completed").GetBoolean() == (outcome is not null),"Conclusione discordante");
            Require(record.Plies == p.Ply && game.GetProperty("plies").GetInt32() == p.Ply,"Ply discordanti");
            if(outcome is not null)
                Require(record.Status == "finished" && record.Winner == outcome.Winner && game.GetProperty("winner").GetInt32() == outcome.Winner && record.Reason == outcome.Reason && game.GetProperty("reason").GetString() == outcome.Reason,"Esito discordante");
            else Require(record.Status == "interrupted" && record.Winner is null,"Interruzione con punti");
            openings.Add((game.GetProperty("opponent").GetString()!, game.GetProperty("number").GetInt32()), string.Join(" ",opening));
        }
        Require(games.Length == 54,"Numero partite diverso da 54");
        var opponents = openings.Keys.Select(x=>x.Item1).Distinct().ToArray();
        Require(opponents.Length == 3,"Numero avversari errato");
        foreach(var opponent in opponents)
        {
            var subset = games.Where(d=>d.RootElement.GetProperty("opponent").GetString()==opponent).ToArray();
            Require(subset.Length == 18 && subset.Count(d=>d.RootElement.GetProperty("white").GetString()=="kingsrow")==9,"Colori non bilanciati");
            for(var n=1;n<=18;n++) {
                Require(openings[(opponent,n)] == openings[(opponents[0],n)],"Aperture differenti fra avversari");
                Require(openings[(opponent,n)] == openings[(opponent,n%2==1?n+1:n-1)],"Aperture non appaiate");
            }
        }
        var report = new { verifiedAt=DateTimeOffset.UtcNow, games=games.Length, moves, uniqueOpeningLines=openings.Values.Distinct().Count(), registered=true, legalReplay=true, balancedColors=true, pairedOpenings=true, resultsMatchReferee=true };
        File.WriteAllText(Path.Combine(directory,"audit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));
        foreach(var document in games) document.Dispose();
    }
    private static void Require(bool ok,string message) { if(!ok) throw new InvalidDataException(message); }
}
