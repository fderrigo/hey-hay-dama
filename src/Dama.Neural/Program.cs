using System.Text.Json;

namespace Dama.Neural;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
        builder.Services.AddSingleton(new KimberliteModel(Path.Combine(builder.Environment.ContentRootPath, "Models", "kimberlite-v1.json")));
        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "ok", model = "Kimberlite v1", protocolVersion = "dama-model/v1" }));
        app.MapPost("/choose-move", async (HttpContext context, KimberliteModel model) =>
        {
            KimberliteRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<KimberliteRequest>(context.RequestAborted);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "JSON non valido." });
            }
            if (request is null) return Results.BadRequest(new { error = "Richiesta vuota." });
            var error = Validate(request);
            if (error is not null) return Results.BadRequest(new { error });
            try
            {
                var decision = model.Choose(request, context.RequestAborted);
                var comment = $"Kimberlite v1: {decision.EvaluatedPositions} posizioni valutate dalla rete neurale.";
                return Results.Ok(new KimberliteResponse("dama-model/v1", decision.MoveId, comment));
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
        });
        app.Run();
    }

    private static string? Validate(KimberliteRequest request)
    {
        if (request.ProtocolVersion != "dama-model/v1") return "Versione del protocollo non supportata.";
        if (request.Rules != "italian-draughts-8x8") return "Regole non supportate.";
        if (request.Player is not ("white" or "black") || request.Position?.Turn != request.Player) return "Giocatore o turno non valido.";
        if (request.Position.Board is null || request.Position.Board.Length != 32 || request.Position.Board.Any(piece => piece is < -2 or > 2)) return "Scacchiera non valida.";
        if (request.ThinkTimeMs is < 100 or > 30000) return "Tempo di decisione non valido.";
        if (request.LegalMoves is null || request.LegalMoves.Length == 0) return "Manca almeno una mossa legale.";
        if (request.LegalMoves.Select(move => move.Id).Distinct(StringComparer.Ordinal).Count() != request.LegalMoves.Length) return "Identificativi delle mosse duplicati.";
        var turn = request.Player == "white" ? 1 : -1;
        foreach (var move in request.LegalMoves)
        {
            if (string.IsNullOrEmpty(move.Id) || move.Id.Length > 128 || move.Path is null || move.Path.Length < 2 || move.Captures is null)
                return "Mossa legale non valida.";
            if (move.From is < 1 or > 32 || move.To is < 1 or > 32 || move.Path.Any(square => square is < 1 or > 32) || move.Captures.Any(square => square is < 1 or > 32))
                return "Coordinate della mossa non valide.";
            if (move.Path[0] != move.From || move.Path[^1] != move.To || Math.Sign(request.Position.Board[move.From - 1]) != turn)
                return "La mossa non corrisponde alla posizione.";
            if (move.Captures.Any(square => Math.Sign(request.Position.Board[square - 1]) != -turn)) return "Presa non valida.";
        }
        return null;
    }
}
