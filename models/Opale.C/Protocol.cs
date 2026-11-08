using System.Text.Json;
using Dama.Engine;

namespace Opale.C;

public sealed record PositionDto(int[] Board, string Turn, int Ply, int QuietKingPlyCount, int RepetitionCount);
public sealed record MoveDto(string Id, int From, int To, int[] Path, int[] Captures, bool Promotes);
public sealed record Request(string ProtocolVersion, string Rules, string Player, PositionDto Position, MoveDto[] LegalMoves, int ThinkTimeMs);

public static class Protocol
{
    public static string? Validate(Request? request)
    {
        if (request is null || request.ProtocolVersion != "dama-model/v1" || request.Rules != "italian-draughts-8x8") return "Protocollo o regole non supportati.";
        if (request.Player is not ("white" or "black") || request.Position is null || request.Position.Turn != request.Player) return "Turno non valido.";
        var board = request.Position.Board;
        if (board is null || board.Length != 32 || board.Any(p => p is < -2 or > 2)) return "Scacchiera non valida.";
        if (request.Position.Ply < 0 || request.Position.QuietKingPlyCount < 0 || request.Position.RepetitionCount < 1 || request.ThinkTimeMs is < 100 or > 30000) return "Contatori o tempo non validi.";
        if (request.LegalMoves is null || request.LegalMoves.Length is < 1 or > 256) return "Mosse mancanti o troppe mosse.";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int side = request.Player == "white" ? 1 : -1;
        foreach (var move in request.LegalMoves)
        {
            if (move is null || string.IsNullOrWhiteSpace(move.Id) || move.Id.Length > 128 || !ids.Add(move.Id)) return "Identificativo mossa non valido.";
            if (move.From is < 1 or > 32 || move.To is < 1 or > 32 || move.Path is null || move.Captures is null || move.Path.Length is < 2 or > 33 || move.Captures.Length > 31) return "Mossa non valida.";
            if (move.Path.Any(s => s is < 1 or > 32) || move.Captures.Any(s => s is < 1 or > 32)) return "Coordinate non valide.";
            if (move.Path[0] != move.From || move.Path[^1] != move.To || Math.Sign(board[move.From - 1]) != side || move.Captures.Any(s => Math.Sign(board[s - 1]) != -side)) return "Mossa incompatibile con la posizione.";
        }
        return null;
    }

    public static Decision Choose(Request request, Model model, CancellationToken cancellation = default)
    {
        int side = request.Player == "white" ? 1 : -1;
        var p = new Position((int[])request.Position.Board.Clone(), side, request.Position.Ply, request.Position.QuietKingPlyCount);
        var moves = request.LegalMoves.Select(m => new Move(m.Path.Select(s => s - 1).ToArray(), m.Captures.Select(s => s - 1).ToArray(), p.Board[m.From - 1], m.Captures.Select(s => Math.Abs(p.Board[s - 1])).ToArray(), m.Promotes)).ToArray();
        return model.Choose(p, moves, cancellation);
    }

    public static async Task<IResult> Handle(HttpContext context, Model model)
    {
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
        Request? request;
        try { request = await context.Request.ReadFromJsonAsync<Request>(context.RequestAborted); }
        catch (JsonException) { return Results.BadRequest(new { error = "JSON non valido." }); }
        var error = Validate(request);
        if (error is not null) return Results.BadRequest(new { error });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(Math.Max(1, request!.ThinkTimeMs - 25));
        try
        {
            var decision = Choose(request, model, timeout.Token);
            // IDs are opaque: map the chosen Move object back by its exact candidate path, never invent an ID.
            int index = Array.FindIndex(request.LegalMoves, m => m.Path.Select(s => s - 1).SequenceEqual(decision.Move.Path) && m.Captures.Select(s => s - 1).SequenceEqual(decision.Move.Captures));
            return Results.Ok(new { protocolVersion = "dama-model/v1", moveId = request.LegalMoves[index].Id, comment = "Opale C: valutazione neurale con pesi manuali e priorita delle prese." });
        }
        catch (OperationCanceledException) { return Results.StatusCode(context.RequestAborted.IsCancellationRequested ? 499 : 408); }
    }
}
