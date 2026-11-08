using Dama.Engine;
using Kingsrow;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
var path = builder.Configuration["Kingsrow:Dll"] ?? throw new InvalidOperationException("Impostare Kingsrow:Dll sul file Kingsrow Italian x64 locale.");
var engine = new NativeEngine(Path.GetFullPath(path));
var app = builder.Build();
app.MapGet("/health", () => new { status = "ok", engine.Name, engine.Sha256, rules = "italian-draughts-8x8" });
app.MapPost("/choose-move", (Request request) =>
{
    if (request.ProtocolVersion != "dama-model/v1" || request.Rules != "italian-draughts-8x8"
        || request.Position?.Board is not { Length: 32 } || request.Position.Board.Any(x => x is < -2 or > 2)
        || request.Position.Board.Count(x => x > 0) > 12 || request.Position.Board.Count(x => x < 0) > 12
        || request.Position.Ply < 0 || request.Position.QuietKingPlyCount < 0
        || request.Player is not ("white" or "black") || request.Position.Turn != request.Player
        || request.ThinkTimeMs is < 100 or > 30000 || request.LegalMoves is not { Length: > 0 and <= 256 })
        return Results.BadRequest(new { error = "Richiesta non valida." });
    var position = new Position(request.Position.Board, request.Player == "white" ? 1 : -1,
        request.Position.Ply, request.Position.QuietKingPlyCount);
    var legal = Rules.LegalMoves(position);
    if (request.LegalMoves.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 128 || m.Path is null || !legal.Any(l => l.Path.Select(x => x + 1).SequenceEqual(m.Path)))
        || request.LegalMoves.Select(m => m.Id).Distinct().Count() != request.LegalMoves.Length)
        return Results.BadRequest(new { error = "Mosse non valide." });
    try
    {
        var chosen = engine.Choose(position, legal, Math.Max(0.05, (request.ThinkTimeMs - 100) / 1000.0));
        var supplied = request.LegalMoves.FirstOrDefault(m => chosen.Path.Select(x => x + 1).SequenceEqual(m.Path));
        return supplied is null ? Results.UnprocessableEntity(new { error = "Mossa Kingsrow assente dai candidati." })
            : Results.Ok(new { protocolVersion = "dama-model/v1", moveId = supplied.Id, comment = "Kingsrow Italian 1.19e: ricerca alpha-beta e libro di aperture." });
    }
    catch (InvalidOperationException e) { return Results.UnprocessableEntity(new { error = e.Message }); }
});
app.Run();

record PositionDto(int[] Board, string Turn, int Ply, int QuietKingPlyCount);
record MoveDto(string Id, int[] Path);
record Request(string ProtocolVersion, string Rules, string Player, PositionDto Position, MoveDto[] LegalMoves, int ThinkTimeMs);
