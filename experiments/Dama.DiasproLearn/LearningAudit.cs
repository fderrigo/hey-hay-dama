using System.Text.Json;
using Dama.Competition;
using Dama.Engine;
using Diaspro;

static class LearningAudit
{
    public static void Run(string directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json"))).RootElement;
        var expectedGames = manifest.GetProperty("games").GetInt32();
        var games = File.ReadLines(Path.Combine(directory, "games.jsonl")).Select(line => JsonDocument.Parse(line)).ToArray();
        var updates = File.ReadLines(Path.Combine(directory, "updates.jsonl")).Select(line => JsonDocument.Parse(line)).ToArray();
        var registry = CompetitionStore.DefaultDirectory();
        var frozen = manifest.TryGetProperty("frozenTacticalFeatures", out var frozenProperty)
            ? frozenProperty.EnumerateArray().Select(value => value.GetInt32()).ToArray() : [];
        var initial = JsonDocument.Parse(File.ReadAllBytes(Path.Combine("models/Diaspro/Models",
            manifest.GetProperty("initialCheckpoint").GetString()! + ".json"))).RootElement;
        var initialCoefficients = initial.GetProperty("coefficients").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var nStep = manifest.TryGetProperty("nStep", out var nStepProperty) ? nStepProperty.GetInt32() : 0;
        foreach (var update in updates)
        {
            var learned = update.RootElement.GetProperty("coefficients").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            Require(frozen.All(index => learned[index] == initialCoefficients[index]), "Coefficiente tattico modificato durante TD.");
        }
        var moves = 0;
        foreach (var document in games)
        {
            var game = document.RootElement;
            var id = game.GetProperty("matchId").GetString()!;
            var record = JsonSerializer.Deserialize<MatchRecord>(File.ReadAllText(Path.Combine(registry, id + ".json")), json)!;
            Require(record.Source == "test" && record.Type == "ai-ai" && record.White?.Id == "ai:diaspro" && record.Black?.Id == "ai:diaspro",
                "Autopartita non registrata correttamente.");
            Require(updates.Count(update => update.RootElement.GetProperty("gameId").GetString() == id)
                == game.GetProperty("updates").GetInt32(), "Aggiornamenti discordanti.");
            var position = Position.Initial();
            var seen = new Dictionary<string, int> { [position.Key] = 1 };
            var frames = game.GetProperty("frames").EnumerateArray().ToArray();
            foreach (var frame in frames.Skip(1))
            {
                var legal = Rules.LegalMoves(position);
                Require(Rules.GetOutcome(position, seen[position.Key], legal) is null, "Mossa dopo la fine.");
                var move = legal.Single(candidate => candidate.Id == frame.GetProperty("moveId").GetString());
                position = Rules.Apply(position, move);
                seen[position.Key] = seen.GetValueOrDefault(position.Key) + 1;
                Require(position.Ply == frame.GetProperty("ply").GetInt32()
                    && position.Board.SequenceEqual(frame.GetProperty("board").EnumerateArray().Select(value => value.GetInt32())),
                    "Stato appreso non coincidente con l'arbitro.");
                moves++;
            }
            var outcome = Rules.GetOutcome(position, seen[position.Key]);
            Require(game.GetProperty("completed").GetBoolean() == (outcome is not null), "Conclusione discordante.");
            Require(record.Plies == position.Ply && game.GetProperty("plies").GetInt32() == position.Ply, "Ply discordanti.");
            if (outcome is not null)
                Require(record.Status == "finished" && record.Winner == outcome.Winner && record.Reason == outcome.Reason
                    && game.GetProperty("winner").GetInt32() == outcome.Winner, "Esito di apprendimento discordante.");
            else Require(record.Status == "interrupted" && record.Winner is null, "Interruzione con esito.");
            if (manifest.TryGetProperty("outcomeOnly", out var outcomeProperty) && outcomeProperty.GetBoolean())
            {
                var gameUpdates = updates.Where(update => update.RootElement.GetProperty("gameId").GetString() == id).ToArray();
                var eligible = frames.Count(frame => frame.TryGetProperty("eligibleForLearning", out var eligibleProperty)
                    && eligibleProperty.GetBoolean());
                Require(gameUpdates.Length == (outcome is null ? 0 : eligible), "Aggiornamenti outcome non coincidenti con gli stati eleggibili.");
                foreach (var update in gameUpdates)
                {
                    var ply = update.RootElement.GetProperty("ply").GetInt32();
                    var frame = frames.Single(item => item.GetProperty("ply").GetInt32() == ply);
                    var turn = frame.GetProperty("turn").GetInt32();
                    var target = outcome!.Winner == 0 ? 0 : outcome.Winner == turn ? 1 : -1;
                    Require(update.RootElement.GetProperty("target").GetSingle() == target, "Target outcome discordante.");
                }
            }
            if (nStep > 0)
            {
                var episodeNetwork = new Network(JsonSerializer.Deserialize<NetworkDefinition>(
                    game.GetProperty("episodeDefinition").GetRawText(), json) ?? throw new InvalidDataException("Rete dell'episodio vuota."));
                var values = game.GetProperty("values").EnumerateArray().ToArray();
                Require(values.Length == frames.Length, "Valori n-step non coincidenti con gli stati.");
                for (var ply = 0; ply < values.Length; ply++)
                {
                    var frame = frames[ply];
                    var value = values[ply];
                    var state = new Position(frame.GetProperty("board").EnumerateArray().Select(item => item.GetInt32()).ToArray(),
                        frame.GetProperty("turn").GetInt32(), ply, frame.GetProperty("quietKingPlyCount").GetInt32());
                    var prediction = MathF.Tanh(episodeNetwork.Evaluate(state, Rules.LegalMoves(state)) / 400f);
                    var recorded = value.GetProperty("prediction").GetSingle();
                    Require(value.GetProperty("ply").GetInt32() == ply && value.GetProperty("turn").GetInt32() == state.Turn
                        && recorded == prediction,
                        $"Valore iniziale n-step non riprodotto: ply={ply}, registrato={recorded}, riprodotto={prediction}.");
                }
                var gameUpdates = updates.Where(update => update.RootElement.GetProperty("gameId").GetString() == id).ToArray();
                var eligible = frames.Count(frame => frame.GetProperty("ply").GetInt32() < position.Ply
                    && frame.TryGetProperty("eligibleForLearning", out var eligibleProperty)
                    && eligibleProperty.GetBoolean() && (outcome is not null
                        || frame.GetProperty("ply").GetInt32() + nStep <= position.Ply));
                Require(gameUpdates.Length == eligible, "Aggiornamenti n-step non coincidenti con gli orizzonti disponibili.");
                foreach (var update in gameUpdates)
                {
                    var ply = update.RootElement.GetProperty("ply").GetInt32();
                    var frame = frames[ply];
                    var turn = frame.GetProperty("turn").GetInt32();
                    Require(update.RootElement.GetProperty("prediction").GetSingle() == values[ply].GetProperty("prediction").GetSingle(),
                        "Predizione n-step discordante.");
                    var futurePly = Math.Min(ply + nStep, position.Ply);
                    float target;
                    if (outcome is not null && futurePly == position.Ply)
                        target = outcome.Winner == 0 ? 0 : outcome.Winner == turn ? 1 : -1;
                    else
                    {
                        var future = values[futurePly].GetProperty("prediction").GetSingle();
                        target = (futurePly - ply) % 2 == 0 ? future : -future;
                    }
                    Require(update.RootElement.GetProperty("target").GetSingle() == target, "Target n-step discordante.");
                }
            }
        }
        Require(games.Length == expectedGames, "Calendario di apprendimento incompleto.");
        var initialPath = Path.Combine("models/Diaspro/Models", manifest.GetProperty("initialCheckpoint").GetString()! + ".json");
        Require(Hash(File.ReadAllBytes(initialPath)) == manifest.GetProperty("initialSha256").GetString(), "Checkpoint iniziale cambiato.");
        var candidateBytes = File.ReadAllBytes(Path.Combine(directory, "candidate.json"));
        var candidate = JsonSerializer.Deserialize<NetworkDefinition>(candidateBytes, json)
            ?? throw new InvalidDataException("Candidato vuoto.");
        var summary = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "summary.json")));
        Require(summary.RootElement.GetProperty("candidateSha256").GetString() == Hash(candidateBytes), "Hash del candidato discordante.");
        Require(frozen.All(index => candidate.Coefficients[index] == initialCoefficients[index]), "Coefficiente tattico cambiato nel candidato.");
        var patternsVerified = false;
        if (manifest.TryGetProperty("usePatterns", out var patternProperty) && patternProperty.GetBoolean())
        {
            Require(candidate.Name.StartsWith("diaspro-v", StringComparison.Ordinal)
                && candidate.PatternSquare is not null && candidate.PatternNear is not null, "Testa di pattern mancante nel candidato.");
            var candidateSquare = candidate.PatternSquare!;
            var candidateNear = candidate.PatternNear!;
            var square = initial.TryGetProperty("patternSquare", out var initialSquare)
                ? initialSquare.EnumerateArray().Select(row => row.EnumerateArray().Select(value => value.GetSingle()).ToArray()).ToArray()
                : Enumerable.Range(0, 4).Select(_ => new float[32]).ToArray();
            var near = initial.TryGetProperty("patternNear", out var initialNear)
                ? initialNear.EnumerateArray().Select(row => row.EnumerateArray().Select(value => value.GetSingle()).ToArray()).ToArray()
                : Enumerable.Range(0, 128).Select(_ => new float[16]).ToArray();
            var gamesById = games.ToDictionary(document => document.RootElement.GetProperty("matchId").GetString()!);
            var learningRate = manifest.GetProperty("patternLearningRate").GetSingle();
            var patternScale = manifest.TryGetProperty("patternScale", out var scaleProperty) ? scaleProperty.GetSingle() : 1f;
            foreach (var document in updates)
            {
                var update = document.RootElement;
                var ply = update.GetProperty("ply").GetInt32();
                var frame = gamesById[update.GetProperty("gameId").GetString()!].RootElement.GetProperty("frames")
                    .EnumerateArray().Single(item => item.GetProperty("ply").GetInt32() == ply);
                var position = new Position(frame.GetProperty("board").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
                    frame.GetProperty("turn").GetInt32(), ply);
                var prediction = update.GetProperty("prediction").GetSingle();
                var delta = patternScale * learningRate * update.GetProperty("error").GetSingle() * (1 - prediction * prediction) / 400f;
                UpdatePatterns(BitPosition.FromPosition(position), delta * 0.5f, square, near);
                UpdatePatterns(BitPosition.FromPosition(position).Rotate(), delta * 0.5f, square, near);
            }
            for (var row = 0; row < square.Length; row++)
            for (var column = 0; column < square[row].Length; column++)
                Require(square[row][column] == candidateSquare[row][column], "Peso casella non riprodotto dall'audit.");
            for (var row = 0; row < near.Length; row++)
            for (var column = 0; column < near[row].Length; column++)
                Require(near[row][column] == candidateNear[row][column], "Peso vicino non riprodotto dall'audit.");
            patternsVerified = true;
        }
        var report = new { verifiedAt = DateTimeOffset.UtcNow, games = games.Length, moves, updates = updates.Length,
            registered = true, legalReplay = true, resultsMatchReferee = true, initialCheckpointVerified = true,
            candidateHashVerified = true, frozenTacticalFeaturesVerified = true, patternsVerified };
        File.WriteAllText(Path.Combine(directory, "audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        summary.Dispose();
        foreach (var document in games.Concat(updates)) document.Dispose();
    }

    private static void UpdatePatterns(BitPosition position, float delta, float[][] square, float[][] near)
    {
        for (var from = 0; from < 32; from++)
        {
            var piece = position.PieceAt(from) * position.Turn;
            if (piece == 0) continue;
            var channel = piece == 1 ? 0 : piece == 2 ? 1 : piece == -1 ? 2 : 3;
            square[channel][from] += delta;
            var (row, column) = Rules.Coordinates(from);
            for (var direction = 0; direction < 4; direction++)
            {
                var to = Rules.Square(row + (direction < 2 ? -1 : 1), column + (direction % 2 == 0 ? -1 : 1));
                if (to < 0) continue;
                var neighbor = position.PieceAt(to) * position.Turn;
                if (neighbor == 0) continue;
                var neighborChannel = neighbor == 1 ? 0 : neighbor == 2 ? 1 : neighbor == -1 ? 2 : 3;
                near[from * 4 + direction][channel * 4 + neighborChannel] += delta;
            }
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
