using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agata;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();

var network = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", "agata-v9.json"));
var temperature = app.Configuration.GetValue("Agata:Temperature", 10.0);
var seed = app.Configuration.GetValue<int?>("Agata:Seed");
var tokenVariable = app.Configuration["Agata:ApiKeyEnvironmentVariable"];
var expected = string.IsNullOrEmpty(tokenVariable) ? null
    : Environment.GetEnvironmentVariable(tokenVariable) ?? throw new InvalidOperationException($"Variabile {tokenVariable} mancante.");

app.MapGet("/health", () => Results.Ok(new { status = "ok", protocolVersion = Protocol.Version }));

app.MapPost("/choose-move", async (HttpContext context) =>
{
    if (expected is not null && !Authorized(context.Request.Headers.Authorization.ToString(), expected))
        return Results.Json(new { error = "Token non valido." }, statusCode: 401);
    if (!context.Request.HasJsonContentType())
        return Results.Json(new { error = "Il corpo deve essere application/json." }, statusCode: 415);

    ChooseMoveRequest? request;
    try { request = await JsonSerializer.DeserializeAsync<ChooseMoveRequest>(context.Request.Body, Protocol.Json, context.RequestAborted); }
    catch (JsonException) { return Results.Json(new { error = "JSON non valido." }, statusCode: 400); }
    catch (BadHttpRequestException ex) { return Results.Json(new { error = "Richiesta troppo grande o interrotta." }, statusCode: ex.StatusCode); }

    var problem = Protocol.Validate(request);
    if (problem is not null) return Results.Json(new { error = problem }, statusCode: 400);

    var board = request!.Position!.Board!;
    var moves = request.LegalMoves!;
    var candidates = moves.Select(m => new Candidate(m.From - 1, m.To - 1, m.Captures!.Select(c => c - 1).ToArray(), m.Promotes)).ToArray();
    var values = Policy.Values(network, board, request.Player == "white" ? 1 : -1, candidates);
    var random = seed is { } fixedSeed ? new Random(Protocol.Mix(fixedSeed, request.GameId, request.Position.Ply)) : Random.Shared;
    return Results.Json(new ChooseMoveResponse(Protocol.Version, moves[Policy.Choose(values, temperature, random)].Id!), Protocol.Json);
});

app.Run();

static bool Authorized(string header, string token) => CryptographicOperations.FixedTimeEquals(
    SHA256.HashData(Encoding.UTF8.GetBytes(header)), SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + token)));

public partial class Program;

namespace Agata
{
    public sealed record PositionDto(int[]? Board, string? Turn, int Ply, int QuietKingPlyCount, int RepetitionCount);
    public sealed record MoveDto(string? Id, int From, int To, int[]? Path, int[]? Captures, bool Promotes);
    public sealed record ChooseMoveRequest(string? ProtocolVersion, string? Rules, string? RequestId, string? GameId, string? Player,
        PositionDto? Position, MoveDto[]? LegalMoves, int ThinkTimeMs);
    public sealed record ChooseMoveResponse(string ProtocolVersion, string MoveId);

    public static class Protocol
    {
        public const string Version = "dama-model/v1", RulesId = "italian-draughts-8x8";
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>Stable across runs, unlike string.GetHashCode, so a fixed seed reproduces a game.</summary>
        public static int Mix(int seed, string? gameId, int ply)
        {
            var hash = (uint)seed * 16777619u ^ 2166136261u;
            foreach (var c in gameId ?? "") hash = (hash ^ c) * 16777619u;
            return (int)((hash ^ (uint)ply) * 16777619u);
        }

        /// <summary>Returns a description of the first problem, or null if the request is usable.</summary>
        public static string? Validate(ChooseMoveRequest? r)
        {
            if (r is null) return "Corpo vuoto.";
            if (r.ProtocolVersion != Version) return "protocolVersion non supportata.";
            if (r.Rules != RulesId) return "rules non supportate.";
            if (r.Player is not ("white" or "black")) return "player non valido.";
            if (r.Position is null || r.Position.Turn != r.Player) return "position.turn deve coincidere con player.";
            if (r.Position.Board is not { Length: 32 } board) return "board deve contenere 32 interi.";
            if (board.Any(v => v is < -2 or > 2)) return "board contiene un valore non valido.";
            if (r.ThinkTimeMs <= 0) return "thinkTimeMs non valido.";
            if (r.LegalMoves is not { Length: > 0 } moves) return "legalMoves è vuoto.";

            var sign = r.Player == "white" ? 1 : -1;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in moves)
            {
                if (string.IsNullOrEmpty(m.Id) || !seen.Add(m.Id)) return "ID mossa mancante o duplicato.";
                if (m.Path is not { Length: >= 2 } path || m.Captures is null) return "Mossa senza percorso.";
                if (path.Any(p => p is < 1 or > 32) || m.Captures.Any(c => c is < 1 or > 32)) return "Casella fuori dall'intervallo 1-32.";
                if (path[0] != m.From || path[^1] != m.To) return "from e to non coincidono con path.";
                if (Math.Sign(board[m.From - 1]) != sign) return "La mossa non parte da un pezzo del giocatore.";
                if (m.Captures.Any(c => Math.Sign(board[c - 1]) != -sign)) return "La presa non indica un pezzo avversario.";
            }
            return null;
        }
    }
}
