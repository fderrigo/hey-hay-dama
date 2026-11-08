using System.Text.Json;
using System.Text.Json.Serialization;
using WeightBuilder;

var version = args.Contains("--v9") ? 9 : args.Contains("--v8") ? 8 : args.Contains("--v7") ? 7 : args.Contains("--v6") ? 6 : args.Contains("--v5") ? 5 : args.Contains("--v4") ? 4 : args.Contains("--v3") ? 3 : 1;
var path = args.FirstOrDefault(a => !a.StartsWith("--")) ?? $"Agata/Models/agata-v{version}.json";
var definition = version switch { 9 => NetworkBuilder.BuildV9(), 8 => NetworkBuilder.BuildV8(), 7 => NetworkBuilder.BuildV7(), 6 => NetworkBuilder.BuildV6(), 5 => NetworkBuilder.BuildV5(), 4 => NetworkBuilder.BuildV4(!args.Contains("--no-landing"), !args.Contains("--no-promote")), 3 => NetworkBuilder.BuildV3(), _ => NetworkBuilder.Build() };
File.WriteAllBytes(path, NetworkFile.Serialize(definition));
Console.WriteLine($"{path}: {definition.Inputs} input, {definition.Neurons.Length} neuroni, {definition.Neurons.Sum(n => n.From.Length)} connessioni");

public static class NetworkFile
{
    public static byte[] Serialize(NetworkDefinition definition) => JsonSerializer.SerializeToUtf8Bytes(definition,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
}
