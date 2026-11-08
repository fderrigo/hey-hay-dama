using System.Text.Json;
using Dama.Engine;

namespace Diaspro;

public sealed record PositionDto(int[]? Board, string? Turn, int Ply, int QuietKingPlyCount, int RepetitionCount);
public sealed record MoveDto(string? Id, int From, int To, int[]? Path, int[]? Captures, bool Promotes);
public sealed record RecentMoveDto(int Ply, string? Player, string? MoveId);
public sealed record ChooseMoveRequest(string? ProtocolVersion, string? Rules, string? RequestId, string? GameId, string? Player,
    PositionDto? Position, MoveDto[]? LegalMoves, int ThinkTimeMs, RecentMoveDto[]? RecentMoves = null);
public sealed record ChooseMoveResponse(string ProtocolVersion, string MoveId, string Comment);

public static class Protocol
{
    public const string Version = "dama-model/v1";
    public const string RulesId = "italian-draughts-8x8";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string? Validate(ChooseMoveRequest? request)
    {
        if (request is null) return "Corpo vuoto.";
        if (request.ProtocolVersion != Version) return "protocolVersion non supportata.";
        if (request.Rules != RulesId) return "rules non supportate.";
        if (request.Player is not ("white" or "black")) return "player non valido.";
        if (request.Position is null || request.Position.Turn != request.Player) return "position.turn deve coincidere con player.";
        if (request.Position.Board is not { Length: 32 } board) return "board deve contenere 32 interi.";
        if (board.Any(v => v is < -2 or > 2)) return "board contiene un valore non valido.";
        if (board.Count(v => v > 0) > 12 || board.Count(v => v < 0) > 12) return "Troppi pezzi per un colore.";
        if (request.Position.Ply < 0 || request.Position.QuietKingPlyCount < 0 || request.Position.RepetitionCount < 0)
            return "Contatori di posizione non validi.";
        if (request.ThinkTimeMs is < 100 or > 30000) return "thinkTimeMs non valido.";
        if (request.LegalMoves is not { Length: > 0 and <= 256 } supplied) return "legalMoves non valido.";
        if (supplied.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 128)) return "ID mossa mancante o troppo lungo.";
        if (supplied.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != supplied.Length) return "ID mossa duplicato.";
        var position = ToPosition(request);
        var legal = Rules.LegalMoves(position);
        foreach (var move in supplied)
        {
            if (move.Path is null || move.Captures is null || move.Path.Length < 2) return "Percorso mossa mancante.";
            if (move.Path[0] != move.From || move.Path[^1] != move.To) return "Estremi del percorso incoerenti.";
            var expected = legal.FirstOrDefault(m => m.Path.Select(s => s + 1).SequenceEqual(move.Path));
            if (expected is null || !expected.Captures.Select(s => s + 1).SequenceEqual(move.Captures)
                || expected.Promotes != move.Promotes) return "Mossa non riconosciuta dall'arbitro.";
        }
        return legal.Count == supplied.Length ? null : "L'elenco non contiene tutte le mosse legali.";
    }

    public static Position ToPosition(ChooseMoveRequest request) => new(request.Position!.Board!,
        request.Player == "white" ? 1 : -1, request.Position.Ply, request.Position.QuietKingPlyCount);
}
