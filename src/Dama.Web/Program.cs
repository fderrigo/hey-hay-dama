using System.Diagnostics;
using System.Threading.RateLimiting;
using Dama.Engine;
using Dama.Web;
using Dama.Competition;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddHttpClient("models", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ModelRegistry>();
builder.Services.AddSingleton<GameStore>();
builder.Services.AddSingleton(provider => new CompetitionStore(builder.Configuration["Arena:CompetitionDirectory"] is { Length: > 0 } path
    ? Path.GetFullPath(path, builder.Environment.ContentRootPath) : CompetitionStore.DefaultDirectory()));
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("writes", context => RateLimitPartition.GetTokenBucketLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new TokenBucketRateLimiterOptions { TokenLimit = 120, TokensPerPeriod = 60, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true }));
});
var app = builder.Build();
_ = app.Services.GetRequiredService<ModelRegistry>();
app.Services.GetRequiredService<GameStore>().Reconcile();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/models", (ModelRegistry models) => Results.Ok(new { models = models.PublicModels, protocolVersion = "dama-model/v1" }));
app.MapGet("/api/leaderboard", (string? source, string? type, CompetitionStore competition) =>
{
    source ??= "all"; type ??= "all";
    return (source != "all" && !CompetitionStore.ValidSource(source)) || !CompetitionStore.ValidType(type)
        ? Results.BadRequest(new { error = "Filtro della classifica non valido." })
        : Results.Ok(competition.GetLeaderboard(source, type));
});

static string? Token(HttpContext context) => context.Request.Headers["X-Game-Token"].FirstOrDefault();
static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

app.MapPost("/api/games", async (CreateGame request, HttpContext context, IConfiguration configuration, ModelRegistry models, GameStore store) =>
{
    if (string.IsNullOrEmpty(request.White) || string.IsNullOrEmpty(request.Black) || !models.Contains(request.White) || !models.Contains(request.Black) || request.Access is not ("online" or "local") || request.HumanSide is not ("white" or "black"))
        return Error(400, "Scegli giocatori e modalità validi.");
    var source = context.Request.Headers["X-Match-Source"].FirstOrDefault() ?? request.Source ?? configuration["Arena:DefaultMatchSource"] ?? "web";
    if (!CompetitionStore.ValidSource(source)) return Error(400, "Origine consentita: web, test o benchmark.");
    if (request.WhiteNickname is not null && Participant.Nickname(request.WhiteNickname) is null
        || request.BlackNickname is not null && Participant.Nickname(request.BlackNickname) is null)
        return Error(400, "Usa un nickname di 2–24 caratteri: lettere, numeri, spazi, punto, trattino o underscore.");
    var owner = GameRoom.NewToken();
    var room = new GameRoom { White = ModelRegistry.CanonicalId(request.White), Black = ModelRegistry.CanonicalId(request.Black), Access = request.Access, Source = source, OwnerHash = GameRoom.Hash(owner) };
    if (request.White == "human" && (request.Black != "human" || request.HumanSide == "white" || request.Access == "local")) room.WhiteSeatHash = room.OwnerHash;
    if (request.Black == "human" && (request.White != "human" || request.HumanSide == "black" || request.Access == "local")) room.BlackSeatHash = room.OwnerHash;
    if (room.WhiteSeatHash is not null) room.WhiteNickname = Participant.Nickname(request.WhiteNickname) ?? $"Giocatore-{room.Id[..6]}";
    if (room.BlackSeatHash is not null) room.BlackNickname = Participant.Nickname(request.BlackNickname) ?? $"Giocatore-{room.Id[6..12]}";
    if (room.WhiteNickname is not null && room.BlackNickname is not null && Participant.Human(room.WhiteNickname).Id == Participant.Human(room.BlackNickname).Id)
        return Error(400, "I due giocatori devono usare nickname diversi.");
    await store.Add(room);
    return Results.Ok(new { token = owner, game = room.View(models, owner) });
}).RequireRateLimiting("writes");

app.MapGet("/api/games/{id}", async (string id, HttpContext context, GameStore store, ModelRegistry models) =>
{
    var room = await store.Find(id);
    if (room is null) return Error(404, "Partita non trovata. Puoi aprire un nuovo tavolo.");
    await room.Gate.WaitAsync(context.RequestAborted);
    try { return Results.Ok(room.View(models, Token(context))); }
    finally { room.Gate.Release(); }
});

app.MapPost("/api/games/{id}/join", async (string id, JoinGame request, HttpContext context, GameStore store, ModelRegistry models) =>
{
    var room = await store.Find(id);
    if (room is null) return Error(404, "Partita non trovata.");
    await room.Gate.WaitAsync(context.RequestAborted);
    try
    {
        if (!room.Waiting || room.IsOwner(Token(context))) return Error(409, "I posti sono già occupati.");
        var nickname = request.Nickname is null ? $"Giocatore-{Guid.NewGuid().ToString("N")[..6]}" : Participant.Nickname(request.Nickname);
        if (nickname is null) return Error(400, "Il nickname deve contenere da 2 a 24 caratteri validi.");
        var opponent = room.WhiteNickname ?? room.BlackNickname;
        if (opponent is not null && Participant.Human(opponent).Id == Participant.Human(nickname).Id)
            return Error(400, "Questo nickname è già usato dall'altro giocatore.");
        var token = GameRoom.NewToken();
        if (room.White == "human" && room.WhiteSeatHash is null) { room.WhiteSeatHash = GameRoom.Hash(token); room.WhiteNickname = nickname; }
        else if (room.Black == "human" && room.BlackSeatHash is null) { room.BlackSeatHash = GameRoom.Hash(token); room.BlackNickname = nickname; }
        else return Error(409, "I posti sono già occupati.");
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await store.Save(room);
        return Results.Ok(new { token, game = room.View(models, token) });
    }
    finally { room.Gate.Release(); }
}).RequireRateLimiting("writes");

app.MapPost("/api/games/{id}/move", async (string id, TurnRequest request, HttpContext context, GameStore store, ModelRegistry models) =>
{
    var room = await store.Find(id);
    if (room is null) return Error(404, "Partita non trovata.");
    await room.Gate.WaitAsync(context.RequestAborted);
    try
    {
        if (room.Position.Ply != request.ExpectedPly || room.Outcome is not null || room.Waiting) return Error(409, "La partita è cambiata. Aggiorna il tavolo.");
        if (room.CurrentPlayer != "human" || !room.Controls(room.Position.Turn, Token(context))) return Error(403, "Non è il tuo turno.");
        var move = Rules.LegalMoves(room.Position).FirstOrDefault(m => m.Id == request.MoveId);
        if (move is null) return Error(400, "Questa mossa non è consentita.");
        room.Play(move);
        await store.Save(room);
        return Results.Ok(room.View(models, Token(context)));
    }
    finally { room.Gate.Release(); }
}).RequireRateLimiting("writes");

app.MapPost("/api/games/{id}/advance", async (string id, TurnRequest request, HttpContext context, GameStore store, ModelRegistry models) =>
{
    var room = await store.Find(id);
    if (room is null) return Error(404, "Partita non trovata.");
    await room.Gate.WaitAsync(context.RequestAborted);
    try
    {
        var token = Token(context);
        if (!room.CanAdvance(token)) return Error(403, "Può avviare il turno soltanto chi partecipa o ha creato la partita.");
        if (room.Position.Ply != request.ExpectedPly || room.Outcome is not null || room.Waiting || room.CurrentPlayer == "human") return Error(409, "Il turno è già cambiato.");
        var timer = Stopwatch.StartNew();
        var legal = Rules.LegalMoves(room.Position);
        try
        {
            var choice = await models.Choose(room.CurrentPlayer, room.Id, room.Position, room.Repetitions.GetValueOrDefault(room.Position.Key, 1), room.History, legal, context.RequestAborted);
            context.RequestAborted.ThrowIfCancellationRequested();
            room.Play(choice.Move, (int)timer.ElapsedMilliseconds, choice.Comment);
            await store.Save(room);
            return Results.Ok(room.View(models, token));
        }
        catch (ModelFailure ex) { return Error(502, ex.Message); }
    }
    finally { room.Gate.Release(); }
}).RequireRateLimiting("writes");

app.MapPost("/api/games/{id}/undo", async (string id, TurnRequest request, HttpContext context, GameStore store, ModelRegistry models) =>
{
    var room = await store.Find(id);
    if (room is null) return Error(404, "Partita non trovata.");
    await room.Gate.WaitAsync(context.RequestAborted);
    try
    {
        var token = Token(context);
        if (!room.IsOwner(token) || (room.White == "human") == (room.Black == "human")) return Error(403, "Puoi annullare solo nelle tue partite di allenamento contro un modello.");
        if (room.Position.Ply != request.ExpectedPly || room.History.Count == 0 || room.Outcome?.Reason == "resigned") return Error(409, "Non ci sono mosse da annullare.");
        var human = room.White == "human" ? "white" : "black";
        var lastHuman = room.History.FindLastIndex(m => m.Player == human);
        if (lastHuman < 0) return Error(409, "Non hai ancora mosso.");
        var replay = room.History.Take(lastHuman).ToArray();
        room.Position = Position.Initial(); room.History.Clear(); room.Outcome = null;
        room.Repetitions = new() { [room.Position.Key] = 1 };
        foreach (var old in replay) room.Play(Rules.LegalMoves(room.Position).Single(m => m.Id == old.MoveId), old.ThinkingMs, old.Comment);
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await store.Save(room);
        return Results.Ok(room.View(models, token));
    }
    finally { room.Gate.Release(); }
}).RequireRateLimiting("writes");

app.MapPost("/api/games/{id}/resign", async (string id, TurnRequest request, HttpContext context, GameStore store, ModelRegistry models) =>
{
    var room = await store.Find(id);
    if (room is null) return Error(404, "Partita non trovata.");
    await room.Gate.WaitAsync(context.RequestAborted);
    try
    {
        var token = Token(context);
        var side = room.White == "human" && room.Controls(1, token) ? 1 : room.Black == "human" && room.Controls(-1, token) ? -1 : 0;
        if (room.Access == "local") side = room.Position.Turn;
        if (side == 0 || !room.Controls(side, token)) return Error(403, "Solo un giocatore può abbandonare.");
        if (room.Outcome is not null || room.Waiting || room.Position.Ply != request.ExpectedPly) return Error(409, "La partita non è in corso.");
        room.Outcome = new Outcome(-side, "resigned");
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await store.Save(room);
        return Results.Ok(room.View(models, token));
    }
    finally { room.Gate.Release(); }
}).RequireRateLimiting("writes");

app.Run();
public partial class Program;
