using System.Text.Json;
using Klinamite;

if (args is ["--write-weights", var path])
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllBytes(path, Network.Serialize(WeightBuilder.Build()));
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();
var network = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", "klinamite-v3.json"));

app.MapGet("/health", () => Results.Ok(new { status = "ok", protocolVersion = Protocol.Version }));
app.MapPost("/choose-move", async (HttpContext context) =>
{
    if (!context.Request.HasJsonContentType()) return Results.Json(new { error = "Il corpo deve essere application/json." }, statusCode: 415);
    ChooseMoveRequest? request;
    try { request = await JsonSerializer.DeserializeAsync<ChooseMoveRequest>(context.Request.Body, Protocol.Json, context.RequestAborted); }
    catch (JsonException) { return Results.Json(new { error = "JSON non valido." }, statusCode: 400); }
    catch (BadHttpRequestException ex) { return Results.Json(new { error = "Richiesta troppo grande o interrotta." }, statusCode: ex.StatusCode); }
    var problem = Protocol.Validate(request);
    if (problem is not null) return Results.Json(new { error = problem }, statusCode: 400);
    var moves = request!.LegalMoves!;
    var candidates = moves.Select(m => new Candidate(m.From - 1, m.To - 1, m.Captures!.Select(c => c - 1).ToArray(), m.Promotes)).ToArray();
    var values = Policy.Values(network, request.Position!.Board!, request.Player == "white" ? 1 : -1, candidates, request.RecentMoves, request.Player);
    return Results.Json(new ChooseMoveResponse(Protocol.Version, moves[Policy.Choose(values)].Id!), Protocol.Json);
});
app.Run();

public partial class Program;

namespace Klinamite
{
    public sealed record PositionDto(int[]? Board, string? Turn, int Ply, int QuietKingPlyCount, int RepetitionCount);
    public sealed record MoveDto(string? Id, int From, int To, int[]? Path, int[]? Captures, bool Promotes);
    public sealed record RecentMoveDto(int Ply, string? Player, string? MoveId);
    public sealed record ChooseMoveRequest(string? ProtocolVersion, string? Rules, string? RequestId, string? GameId, string? Player,
        PositionDto? Position, MoveDto[]? LegalMoves, int ThinkTimeMs, RecentMoveDto[]? RecentMoves = null);
    public sealed record ChooseMoveResponse(string ProtocolVersion, string MoveId);

    public static class Protocol
    {
        public const string Version = "dama-model/v1", RulesId = "italian-draughts-8x8";
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
