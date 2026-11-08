using System.Text.Json;
using Dama.Engine;
using Diaspro;

if (args is ["--write-weights", var path])
{
    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllBytes(fullPath, ManualWeights.Serialize(ManualWeights.Build()));
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();
var network = Network.Load(Path.Combine(AppContext.BaseDirectory, "Models", "diaspro-v4.json"));
var memory = new PositionMemory();

app.MapGet("/health", () => Results.Ok(new { status = "ok", protocolVersion = Protocol.Version, model = network.Name,
    architecture = "relational-graph", inputs = network.InputCount, parameters = network.ParameterCount, search = false, training = false }));
app.MapPost("/choose-move", async (HttpContext context) =>
{
    if (!context.Request.HasJsonContentType()) return Results.Json(new { error = "Il corpo deve essere application/json." }, statusCode: 415);
    ChooseMoveRequest? request;
    try { request = await JsonSerializer.DeserializeAsync<ChooseMoveRequest>(context.Request.Body, Protocol.Json, context.RequestAborted); }
    catch (JsonException) { return Results.Json(new { error = "JSON non valido." }, statusCode: 400); }
    catch (BadHttpRequestException ex) { return Results.Json(new { error = "Richiesta troppo grande o interrotta." }, statusCode: ex.StatusCode); }
    catch (OperationCanceledException) { return Results.Empty; }
    var problem = Protocol.Validate(request);
    if (problem is not null) return Results.Json(new { error = problem }, statusCode: 400);
    var position = Protocol.ToPosition(request!);
    var legal = Rules.LegalMoves(position);
    var values = Policy.Values(network, position, legal, memory, request!.GameId);
    var chosen = legal[Policy.Choose(values)];
    memory.Commit(request.GameId, position, Rules.Apply(position, chosen));
    var supplied = request.LegalMoves!.First(m => chosen.Path.Select(s => s + 1).SequenceEqual(m.Path!));
    return Results.Json(new ChooseMoveResponse(Protocol.Version, supplied.Id!, "Valutazione relazionale e memoria episodica; nessuna ricerca."), Protocol.Json);
});
app.Run();

public partial class Program;
