using System.Text.Json;
using Dama.Competition;
using Dama.Engine;

static class Audit
{
    public static void Run(string directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json"))).RootElement;
        var source = manifest.GetProperty("source").GetString();
        var expectedGames = manifest.GetProperty("games").GetInt32();
        var registry = CompetitionStore.DefaultDirectory();
        var games = File.ReadLines(Path.Combine(directory, "games.jsonl")).Select(line => JsonDocument.Parse(line)).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var openings = new Dictionary<int, string>();
        var colors = new Dictionary<int, (string White, string Black)>();
        var moves = 0;
        foreach (var document in games)
        {
            var game = document.RootElement;
            var id = game.GetProperty("matchId").GetString()!;
            Require(ids.Add(id), "ID duplicato.");
            var record = JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(Path.Combine(registry, id + ".json")), json)!;
            Require(record.Source == source && record.Type == "ai-ai", "Origine o tipo errato.");
            Require(record.White!.Id == "ai:" + game.GetProperty("white").GetString()
                && record.Black!.Id == "ai:" + game.GetProperty("black").GetString(), "Partecipanti discordanti.");
            var position = Position.Initial();
            var seen = new Dictionary<string, int> { [position.Key] = 1 };
            var frames = game.GetProperty("frames").EnumerateArray().ToArray();
            Require(frames[0].GetProperty("board").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(position.Board), "Posizione iniziale errata.");
            var opening = new List<string>();
            foreach (var frame in frames.Skip(1))
            {
                var legal = Rules.LegalMoves(position);
                Require(Rules.GetOutcome(position, seen[position.Key], legal) is null, "Mossa dopo la fine della partita.");
                var move = legal.Single(m => m.Id == frame.GetProperty("moveId").GetString());
                position = Rules.Apply(position, move);
                seen[position.Key] = seen.GetValueOrDefault(position.Key) + 1;
                Require(position.Board.SequenceEqual(frame.GetProperty("board").EnumerateArray().Select(v => v.GetInt32()))
                    && position.Ply == frame.GetProperty("ply").GetInt32() && position.Turn == frame.GetProperty("turn").GetInt32(), "Stato discordante.");
                if (position.Ply <= 6) opening.Add(move.Id);
                moves++;
            }
            var outcome = Rules.GetOutcome(position, seen[position.Key]);
            Require(game.GetProperty("completed").GetBoolean() == (outcome is not null), "Conclusione discordante.");
            Require(record.Plies == position.Ply && game.GetProperty("plies").GetInt32() == position.Ply, "Ply discordanti.");
            if (outcome is not null)
                Require(record.Status == "finished" && record.Winner == outcome.Winner && game.GetProperty("winner").GetInt32() == outcome.Winner
                    && record.Reason == outcome.Reason && game.GetProperty("reason").GetString() == outcome.Reason, "Esito discordante.");
            else Require(record.Status == "interrupted" && record.Winner is null, "Interruzione con punti.");
            var pair = (game.GetProperty("number").GetInt32() - 1) / 2;
            var openingKey = string.Join(" ", opening);
            if (!openings.TryAdd(pair, openingKey)) Require(openings[pair] == openingKey, "Apertura non appaiata.");
            var currentColors = (White: game.GetProperty("white").GetString()!, Black: game.GetProperty("black").GetString()!);
            if (!colors.TryAdd(pair, currentColors)) Require(colors[pair].White == currentColors.Black && colors[pair].Black == currentColors.White, "Colori non invertiti.");
        }
        Require(games.Length == expectedGames, "Calendario incompleto.");
        Require(games.Count(g => g.RootElement.GetProperty("white").GetString() == "diaspro") == expectedGames / 2, "Colori non bilanciati.");
        var model = manifest.GetProperty("diaspro");
        var version = model.GetProperty("version").GetString()!;
        var expectedHash = model.GetProperty("sha256").GetString();
        var actualHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes($"models/Diaspro/Models/{version}.json")));
        Require(expectedHash == actualHash, "Checkpoint diverso dal manifest.");
        var sourcesVerified = manifest.TryGetProperty("diasproSources", out var sourceHashes);
        if (sourcesVerified)
            foreach (var sourceEntry in sourceHashes.EnumerateObject())
            {
                var sourceHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourceEntry.Name)));
                Require(sourceEntry.Value.GetString() == sourceHash, $"Sorgente diverso dal manifest: {sourceEntry.Name}");
            }
        var pairScores = games.GroupBy(game => (game.RootElement.GetProperty("number").GetInt32() - 1) / 2)
            .Select(pair => pair.Average(game => Score(game.RootElement))).ToArray();
        var score = pairScores.Average();
        var standardDeviation = pairScores.Length > 1
            ? Math.Sqrt(pairScores.Sum(value => (value - score) * (value - score)) / (pairScores.Length - 1)) : 0;
        var margin = 1.96 * standardDeviation / Math.Sqrt(pairScores.Length);
        const double z = 1.96;
        var wilsonDenominator = 1 + z * z / games.Length;
        var wilsonCenter = (score + z * z / (2 * games.Length)) / wilsonDenominator;
        var wilsonMargin = z * Math.Sqrt(score * (1 - score) / games.Length + z * z / (4 * games.Length * games.Length)) / wilsonDenominator;
        var wins = games.Count(game => Score(game.RootElement) == 1);
        var draws = games.Count(game => Score(game.RootElement) == 0.5);
        var losses = games.Count(game => game.RootElement.GetProperty("completed").GetBoolean() && Score(game.RootElement) == 0);
        var interrupted = games.Count(game => !game.RootElement.GetProperty("completed").GetBoolean());
        var report = new
        {
            verifiedAt = DateTimeOffset.UtcNow, games = games.Length, moves, uniqueOpeningLines = openings.Values.Distinct().Count(),
            registered = true, legalReplay = true, balancedColors = true, pairedOpenings = true, resultsMatchReferee = true,
            checkpointVersion = version, checkpointSha256 = actualHash, checkpointMatchesManifest = true, sourcesVerified,
            wins, draws, losses, interrupted, score, pairScoreStandardDeviation = standardDeviation,
            pairScore95Margin = margin, pairScore95Low = Math.Max(0, score - margin), pairScore95High = Math.Min(1, score + margin),
            wilsonScore95Low = Math.Max(0, wilsonCenter - wilsonMargin), wilsonScore95High = Math.Min(1, wilsonCenter + wilsonMargin)
        };
        File.WriteAllText(Path.Combine(directory, "audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        foreach (var document in games) document.Dispose();
    }

    private static double Score(JsonElement game)
    {
        if (!game.GetProperty("completed").GetBoolean()) return 0;
        var winner = game.GetProperty("winner").GetInt32();
        if (winner == 0) return 0.5;
        return (winner == 1 ? game.GetProperty("white").GetString() : game.GetProperty("black").GetString()) == "diaspro" ? 1 : 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
