namespace Dama.Neural;

public sealed record KimberliteMove(string Id, int From, int To, int[] Path, int[] Captures, bool Promotes);
public sealed record KimberlitePosition(int[] Board, string Turn, int Ply, int QuietKingPlyCount, int RepetitionCount);
public sealed record KimberliteRecentMove(int Ply, string Player, string MoveId);
public sealed record KimberliteRequest(
    string ProtocolVersion,
    string Rules,
    string RequestId,
    string GameId,
    string Player,
    KimberlitePosition Position,
    KimberliteMove[] LegalMoves,
    KimberliteRecentMove[] RecentMoves,
    int ThinkTimeMs);
public sealed record KimberliteResponse(string ProtocolVersion, string MoveId, string Comment);
public sealed record KimberliteDecision(string MoveId, double Score, int EvaluatedPositions);
