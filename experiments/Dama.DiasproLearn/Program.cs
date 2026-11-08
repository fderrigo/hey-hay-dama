using System.Text.Json;
using Dama.Competition;
using Dama.Engine;
using Diaspro;

if (args is ["--audit", var auditDirectory]) { LearningAudit.Run(auditDirectory); return; }
if (args.Length < 1) throw new ArgumentException("Uso: directory [--games N] [--seed N] [--milliseconds N] [--ply-limit N] [--pattern] [--outcome | --n-step N], --audit directory.");
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new InvalidOperationException("La directory di apprendimento deve essere nuova.");
Directory.CreateDirectory(output);
var games = Option("--games", 100);
var seed = Option("--seed", 15001);
var milliseconds = Option("--milliseconds", 50);
var usePatterns = args.Contains("--pattern");
var outcomeOnly = args.Contains("--outcome");
var nStep = Option("--n-step", 0);
if ((outcomeOnly || nStep > 0) && !usePatterns || outcomeOnly && nStep > 0 || nStep < 0)
    throw new ArgumentException("--outcome e --n-step N>0 richiedono --pattern e sono alternativi.");
var episodeFrozen = outcomeOnly || nStep > 0;
const int openingPlies = 6;
var plyLimit = Option("--ply-limit", 240);
const double exploration = 0.06;
const float learningRate = 0.8f;
const float patternLearningRate = 2f;
var scales = new[] { 1f, 12f, 12f, 7f, 12f, 12f, 12f, 12f, 12f, 672f, 672f, 224f, 36f, 36f, 12f, 12f, 1f, 36f, 12f };
var random = new Random(seed);
var store = new CompetitionStore(CompetitionStore.DefaultDirectory());
var initialVersion = nStep > 0 ? "diaspro-v21-outcome" : outcomeOnly ? "diaspro-v20-pattern-guarded" : usePatterns ? "diaspro-v18-bitnet" : "diaspro-v13";
var initialDefinition = JsonSerializer.Deserialize<NetworkDefinition>(File.ReadAllBytes($"models/Diaspro/Models/{initialVersion}.json"),
    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Checkpoint iniziale vuoto.");
var coefficients = (float[])initialDefinition.Coefficients.Clone();
var patternSquare = usePatterns ? initialDefinition.PatternSquare?.Select(row => (float[])row.Clone()).ToArray()
    ?? Enumerable.Range(0, 4).Select(_ => new float[32]).ToArray() : null;
var patternNear = usePatterns ? initialDefinition.PatternNear?.Select(row => (float[])row.Clone()).ToArray()
    ?? Enumerable.Range(0, 128).Select(_ => new float[16]).ToArray() : null;
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
{
    startedAt = DateTimeOffset.UtcNow, algorithm = nStep > 0
        ? "episode-frozen n-step value regression, strategic linear head plus symmetric square/near-pattern head"
        : outcomeOnly ? "episode-end outcome regression, strategic linear head plus symmetric square/near-pattern head"
        : usePatterns ? "online TD(0), strategic linear head plus symmetric square/near-pattern head"
            : "online TD(0), scalar state value, linear head only", games, seed,
    initialCheckpoint = initialVersion, initialSha256 = Hash($"models/Diaspro/Models/{initialVersion}.json"),
    trainingData = "registered Diaspro self-play from the initial position only", externalPositions = false,
    kingsrowSupervision = false, openingPlies, exploration, milliseconds, plyLimit, learningRate, outcomeOnly, nStep,
    interruptedGamesLearned = !outcomeOnly, usePatterns, patternLearningRate, patternScale = Network.PatternScale,
    patternSquareWeights = 128, patternNearWeights = 2048,
    frozenTacticalFeatures = new[] { 9, 10, 11, 12, 17, 18 },
    valueScale = 400, featureScales = scales, competitionDirectory = store.DirectoryPath
}, new JsonSerializerOptions(json) { WriteIndented = true }));

using var gameStream = new StreamWriter(Path.Combine(output, "games.jsonl"));
using var updateStream = new StreamWriter(Path.Combine(output, "updates.jsonl"));
var completedGames = 0;
for (var gameNumber = 0; gameNumber < games; gameNumber++)
{
    var participantName = nStep > 0 ? "Diaspro n-step self-play" : outcomeOnly ? "Diaspro outcome self-play" : "Diaspro TD self-play";
    using var tracked = store.Start("test", Participant.Ai("diaspro", participantName), Participant.Ai("diaspro", participantName));
    var position = Position.Initial();
    var seen = new Dictionary<string, int> { [position.Key] = 1 };
    var memory = new PositionMemory();
    var frames = new List<object> { new { ply = 0, board = position.Board, turn = position.Turn, quietKingPlyCount = position.QuietKingPlyCount } };
    var pending = new List<Experience>();
    var episodeValues = new List<EpisodeValue>();
    var episodeDefinition = Definition(coefficients);
    var episodeNetwork = episodeFrozen ? new Network(episodeDefinition) : null;
    var updateCount = 0;
    var absoluteError = 0.0;
    Outcome? outcome = null;
    void RecordUpdate(Experience experience, float target)
    {
        var error = target - experience.Prediction;
        ApplyUpdate(experience.Position, experience.Features, target, experience.Prediction);
        updateStream.WriteLine(JsonSerializer.Serialize(new
        {
            gameId = tracked.Id, ply = experience.Position.Ply, position = experience.Position.Key, moveId = experience.Move.Id,
            experience.Exploratory, target, prediction = experience.Prediction, error, coefficients = coefficients.ToArray()
        }, json));
        updateCount++;
        absoluteError += Math.Abs(error);
    }
    while (true)
    {
        var network = episodeNetwork ?? new Network(Definition(coefficients));
        var legal = Rules.LegalMoves(position);
        var value = MathF.Tanh(network.Evaluate(position, legal) / 400f);
        episodeValues.Add(new EpisodeValue(position.Ply, position.Turn, value));
        outcome = Rules.GetOutcome(position, seen.GetValueOrDefault(position.Key), legal);
        if (outcome is not null || position.Ply >= plyLimit) break;
        var opening = position.Ply < openingPlies;
        var exploratory = !opening && random.NextDouble() < exploration;
        var chosen = opening || exploratory
            ? legal[random.Next(legal.Count)]
            : legal[DeepSearch.Choose(network, position, legal, memory, tracked.Id, milliseconds,
                seen.GetValueOrDefault(position.Key), 16)];
        var features = network.Features(position);
        var next = Rules.Apply(position, chosen);
        seen[next.Key] = seen.GetValueOrDefault(next.Key) + 1;
        var nextOutcome = Rules.GetOutcome(next, seen[next.Key]);
        float target;
        if (nextOutcome is not null)
            target = nextOutcome.Winner == 0 ? 0 : nextOutcome.Winner == position.Turn ? 1 : -1;
        else target = -MathF.Tanh(network.Evaluate(next) / 400f);
        if (!opening)
        {
            var experience = new Experience(position, chosen, exploratory, features, value);
            if (episodeFrozen) pending.Add(experience);
            else RecordUpdate(experience, target);
        }
        memory.Commit(tracked.Id, position, next);
        frames.Add(new { ply = next.Ply, board = next.Board, turn = next.Turn, quietKingPlyCount = next.QuietKingPlyCount,
            moveId = chosen.Id, eligibleForLearning = next.Ply >= openingPlies });
        position = next;
    }
    if (outcomeOnly && outcome is not null)
        foreach (var experience in pending)
        {
            var target = outcome.Winner == 0 ? 0 : outcome.Winner == experience.Position.Turn ? 1 : -1;
            RecordUpdate(experience, target);
        }
    if (nStep > 0)
        foreach (var experience in pending)
        {
            var futurePly = experience.Position.Ply + nStep;
            if (futurePly > position.Ply)
            {
                if (outcome is null) continue;
                futurePly = position.Ply;
            }
            float target;
            if (outcome is not null && futurePly == position.Ply)
                target = outcome.Winner == 0 ? 0 : outcome.Winner == experience.Position.Turn ? 1 : -1;
            else
            {
                var future = episodeValues[futurePly];
                target = (futurePly - experience.Position.Ply) % 2 == 0 ? future.Prediction : -future.Prediction;
            }
            RecordUpdate(experience, target);
        }
    if (outcome is not null) tracked.Complete(outcome.Winner, position.Ply, outcome.Reason);
    else tracked.Interrupt(position.Ply, "ply-limit");
    gameStream.WriteLine(JsonSerializer.Serialize(new
    {
        matchId = tracked.Id, number = gameNumber + 1, completed = outcome is not null, winner = outcome?.Winner,
        reason = outcome?.Reason ?? "ply-limit", plies = position.Ply, updates = updateCount,
        meanAbsoluteError = updateCount == 0 ? 0 : absoluteError / updateCount, frames, values = episodeValues,
        episodeDefinition = nStep > 0 ? episodeDefinition : null
    }, json));
    await gameStream.FlushAsync();
    await updateStream.FlushAsync();
    File.WriteAllBytes(Path.Combine(output, "candidate.json"), ManualWeights.Serialize(Definition(coefficients)));
    completedGames++;
    Console.WriteLine($"{completedGames}/{games} {tracked.Id} {(outcome is null ? "interrotta" : outcome.Winner == 0 ? "patta" : outcome.Winner == 1 ? "bianco" : "nero")} ply={position.Ply} updates={updateCount}");
}

var finalDefinition = Definition(coefficients);
File.WriteAllBytes(Path.Combine(output, "candidate.json"), ManualWeights.Serialize(finalDefinition));
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
{
    completedGames, updateBytes = updateStream.BaseStream.Length, candidateSha256 = Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(ManualWeights.Serialize(finalDefinition))),
    initialDefinition.Coefficients, learnedCoefficients = coefficients
}, new JsonSerializerOptions(json) { WriteIndented = true }));

void ApplyUpdate(Position position, float[] features, float target, float prediction)
{
    var error = target - prediction;
    for (var index = 0; index < coefficients.Length; index++)
    {
        if (index is 9 or 10 or 11 or 12 or 17 or 18) continue;
        coefficients[index] += learningRate * error * (1 - prediction * prediction) / 400f * features[index] / scales[index];
    }
    if (usePatterns) UpdatePatterns(BitPosition.FromPosition(position),
        Network.PatternScale * patternLearningRate * error * (1 - prediction * prediction) / 400f);
    if (coefficients.Any(coefficient => !float.IsFinite(coefficient))
        || usePatterns && (patternSquare!.Any(row => row.Any(weight => !float.IsFinite(weight)))
            || patternNear!.Any(row => row.Any(weight => !float.IsFinite(weight)))))
        throw new InvalidOperationException("Coefficiente non finito.");
}

void UpdatePatterns(BitPosition position, float delta)
{
    UpdatePatternRaw(position, delta * 0.5f);
    UpdatePatternRaw(position.Rotate(), delta * 0.5f);
}

void UpdatePatternRaw(BitPosition position, float delta)
{
    for (var from = 0; from < 32; from++)
    {
        var piece = position.PieceAt(from) * position.Turn;
        if (piece == 0) continue;
        var channel = piece == 1 ? 0 : piece == 2 ? 1 : piece == -1 ? 2 : 3;
        patternSquare![channel][from] += delta;
        var (row, column) = Rules.Coordinates(from);
        for (var direction = 0; direction < 4; direction++)
        {
            var to = Rules.Square(row + (direction < 2 ? -1 : 1), column + (direction % 2 == 0 ? -1 : 1));
            if (to < 0) continue;
            var neighbor = position.PieceAt(to) * position.Turn;
            if (neighbor == 0) continue;
            var neighborChannel = neighbor == 1 ? 0 : neighbor == 2 ? 1 : neighbor == -1 ? 2 : 3;
            patternNear![from * 4 + direction][channel * 4 + neighborChannel] += delta;
        }
    }
}

int Option(string name, int fallback) { var index = Array.IndexOf(args, name); return index >= 0 ? int.Parse(args[index + 1]) : fallback; }
NetworkDefinition Definition(float[] learned) => initialDefinition with
{
    Name = nStep > 0 ? $"diaspro-v22-nstep{nStep}" : outcomeOnly ? "diaspro-v21-outcome" : usePatterns ? "diaspro-v20-pattern-guarded" : "diaspro-v16-td-safe",
    Construction = nStep > 0
        ? $"Target {nStep}-step da autopartite registrate a pesi costanti per episodio; esito terminale nell'orizzonte, altrimenti valore futuro; tattica v18 congelata."
        : outcomeOnly ? "Esiti di autopartite registrate, aggiornamento a fine episodio senza bootstrap; pattern simmetrici 0,5 e tattica v18 congelata."
        : usePatterns ? "TD(0) da autopartite registrate; pattern casella/vicini simmetrici con scala 0,5; salvaguardie tattiche v18 congelate."
            : "TD(0) su autopartite registrate; testa strategica appresa, sei coefficienti tattici congelati da v13; nessuna supervisione esterna.",
    Coefficients = (float[])learned.Clone(),
    PatternSquare = usePatterns ? patternSquare!.Select(row => (float[])row.Clone()).ToArray() : null,
    PatternNear = usePatterns ? patternNear!.Select(row => (float[])row.Clone()).ToArray() : null
};
static string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

record Experience(Position Position, Move Move, bool Exploratory, float[] Features, float Prediction);
record EpisodeValue(int Ply, int Turn, float Prediction);
