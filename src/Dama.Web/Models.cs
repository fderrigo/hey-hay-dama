using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dama.Engine;

namespace Dama.Web;

public sealed class ModelConfiguration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "builtin";
    public string Strategy { get; set; } = "network";
    public string? WeightsPath { get; set; }
    public int MaxThinkTimeMs { get; set; } = 1500;
    public int HttpGraceMs { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Endpoint { get; set; }
    public string? ApiKeyEnvironmentVariable { get; set; }
}

public sealed record MoveView(string Id, int From, int To, int[] Path, int[] Captures, bool Promotes)
{
    public static MoveView FromMove(Move m) => new(m.Id, m.From + 1, m.To + 1, m.Path.Select(x => x + 1).ToArray(), m.Captures.Select(x => x + 1).ToArray(), m.Promotes);
}
public sealed record PositionView(int[] Board, string Turn, int Ply, int QuietKingPlyCount, int RepetitionCount);
public sealed record RecentMove(int Ply, string Player, string MoveId);
public sealed record ModelRequest(string ProtocolVersion, string Rules, string RequestId, string GameId, string Player, PositionView Position, MoveView[] LegalMoves, RecentMove[] RecentMoves, int ThinkTimeMs);
public sealed record ModelResponse(string? ProtocolVersion, string? MoveId, string? Comment);
public sealed record ModelChoice(Move Move, string? Comment, long Nodes, int Depth);
public sealed class ModelFailure(string message) : Exception(message);

public sealed class ModelRegistry
{
    private readonly Dictionary<string, ModelConfiguration> definitions;
    private readonly Dictionary<string, NeuralModel> networks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> tokens = new(StringComparer.Ordinal);
    private readonly IHttpClientFactory clients;
    public IEnumerable<object> PublicModels => definitions.Values.Select(m => new { m.Id, m.Name, m.Description });
    public int Count => definitions.Count;

    public ModelRegistry(IConfiguration configuration, IWebHostEnvironment environment, IHttpClientFactory clients)
    {
        this.clients = clients;
        var models = configuration.GetSection("Arena:Models").Get<ModelConfiguration[]>() ?? [];
        if (models.GroupBy(x => x.Id, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new InvalidOperationException("Gli ID dei modelli devono essere univoci.");
        definitions = models.Where(m => m.Enabled).ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var m in definitions.Values)
        {
            if (!Regex.IsMatch(m.Id, "^[a-z0-9][a-z0-9-]{0,47}$") || m.Id == "human" || string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 60 || m.Description.Length > 180 || m.MaxThinkTimeMs is < 100 or > 30000 || m.HttpGraceMs is < 0 or > 5000)
                throw new InvalidOperationException($"Configurazione non valida per il modello {m.Id}.");
            if (m.Kind == "builtin")
            {
                if (m.Strategy != "network" || string.IsNullOrWhiteSpace(m.WeightsPath))
                    throw new InvalidOperationException($"Strategia o pesi non validi: {m.Id}.");
                networks[m.Id] = NeuralModel.Load(Path.GetFullPath(m.WeightsPath, environment.ContentRootPath));
            }
            else if (m.Kind == "http")
            {
                if (!Uri.TryCreate(m.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidOperationException($"Endpoint non valido: {m.Id}.");
                if (!string.IsNullOrEmpty(m.ApiKeyEnvironmentVariable))
                    tokens[m.Id] = Environment.GetEnvironmentVariable(m.ApiKeyEnvironmentVariable) ?? throw new InvalidOperationException($"Variabile {m.ApiKeyEnvironmentVariable} mancante.");
            }
            else throw new InvalidOperationException($"Tipo di modello non supportato: {m.Id}.");
        }
    }

    // Preserve already saved rooms after the model's public rename.
    public static string CanonicalId(string id) => id == "ambra" ? "opale" : id;
    public bool Contains(string id) => id == "human" || definitions.ContainsKey(CanonicalId(id));
    public string Name(string id) => id == "human" ? "Giocatore" : definitions.TryGetValue(CanonicalId(id), out var m) ? m.Name : id;

    public async Task<ModelChoice> Choose(string id, string gameId, Position p, int repetitions, IReadOnlyList<MoveRecord> history, List<Move> legal, CancellationToken cancellation)
    {
        id = CanonicalId(id);
        if (!definitions.TryGetValue(id, out var config)) throw new ModelFailure("Questo modello non è più disponibile. Il gestore deve riabilitarlo.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        // Transport grace is not sent to the engine as extra thinking time.
        deadline.CancelAfter(config.MaxThinkTimeMs + (config.Kind == "http" ? config.HttpGraceMs : 0));
        if (config.Kind == "builtin")
        {
            var decision = await Task.Run(() => networks[id].Choose(p, legal), cancellation);
            cancellation.ThrowIfCancellationRequested();
            return new ModelChoice(decision.Move, null, decision.Nodes, decision.Depth);
        }
        var player = p.Turn == 1 ? "white" : "black";
        var request = new ModelRequest("dama-model/v1", "italian-draughts-8x8", Guid.NewGuid().ToString("N"), gameId, player,
            new PositionView(p.Board, player, p.Ply, p.QuietKingPlyCount, repetitions), legal.Select(MoveView.FromMove).ToArray(),
            history.TakeLast(16).Select(m => new RecentMove(m.Ply, m.Player, m.MoveId)).ToArray(), config.MaxThinkTimeMs);
        using var message = new HttpRequestMessage(HttpMethod.Post, config.Endpoint) { Content = JsonContent.Create(request) };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (tokens.TryGetValue(id, out var token)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await clients.CreateClient("models").SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw new ModelFailure($"Il modello ha risposto con un errore HTTP {(int)response.StatusCode}. Puoi riprovare.");
            if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new ModelFailure("Il modello deve rispondere con application/json.");
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var bytes = new byte[16385];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token);
                if (read == 0) break;
                count += read;
            }
            if (count > 16384) throw new ModelFailure("La risposta del modello supera 16 KiB.");
            var answer = JsonSerializer.Deserialize<ModelResponse>(bytes.AsSpan(0, count), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (answer?.ProtocolVersion != "dama-model/v1" || answer.Comment?.Length > 240) throw new ModelFailure("La risposta del modello non rispetta dama-model/v1.");
            var move = legal.FirstOrDefault(m => m.Id == answer.MoveId);
            if (move is null) throw new ModelFailure("Il modello ha indicato una mossa non consentita. La posizione è rimasta invariata.");
            return new ModelChoice(move, answer.Comment, 0, 0);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new ModelFailure("Il modello ha superato il tempo disponibile. Puoi riprovare."); }
        catch (HttpRequestException) { throw new ModelFailure("Il servizio del modello non è raggiungibile. Puoi riprovare."); }
        catch (JsonException) { throw new ModelFailure("Il modello ha restituito JSON non valido."); }
    }
}
