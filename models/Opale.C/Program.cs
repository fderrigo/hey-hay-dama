using Opale.C;

if (args is ["--test"]) { Verification.Run(); return; }
if (args is ["--export", var exportPath])
{
    var network = new Network(ManualBuilder.Build());
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exportPath))!);
    network.Save(exportPath);
    Console.WriteLine($"Exported {network.Definition.Units.Length} units, {network.Parameters} parameters to {exportPath}");
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
string path = builder.Configuration["weights"] ?? Path.Combine(AppContext.BaseDirectory, "Models", "opale-c-v2.json");
var loaded = Network.Load(path);
builder.Services.AddSingleton(new Model(loaded));
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", model = loaded.Definition.Name, protocolVersion = "dama-model/v1", units = loaded.Definition.Units.Length, parameters = loaded.Parameters }));
app.MapPost("/choose-move", Protocol.Handle);
app.Run();
