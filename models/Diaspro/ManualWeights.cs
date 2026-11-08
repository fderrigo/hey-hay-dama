using System.Text.Json;

namespace Diaspro;

public sealed record NetworkDefinition(string Format, string Name, string Construction, string[] Features, float[] Coefficients,
    float[][] GraphSelf, float[][] GraphNear, float[][] GraphJump, float[] GraphHead,
    float[][]? PatternSquare = null, float[][]? PatternNear = null);

public static class ManualWeights
{
    public const string Version = "diaspro-v21-outcome";
    public static readonly string[] FeatureNames = [
        "bias", "material", "advancement", "promotion-race", "back-rank", "centrality", "king-centrality",
        "quiet-mobility", "safe-mobility", "immediate-capture", "potential-capture", "capture-promotion",
        "exposed-material", "supported-material", "edge-men", "crowding", "quiet-king-pressure",
        "undefended-exposure", "continued-capture"
    ];

    public static NetworkDefinition Build() => new("diaspro-relational/v1", Version,
        "Esiti di autopartite registrate, aggiornamento a fine episodio senza bootstrap; pattern simmetrici 0,5 e tattica v18 congelata.",
        FeatureNames,
        [-11.361788f, 112.32495f, 5.23508f, 8.994837f, 4.0040092f, 3.1725068f, 5.011599f, 5.17896f, 8.178825f,
            0.92f, 0.28f, 0.72f, 32, 4.0053153f, -1.9643818f, -3.0311174f, 0.3845101f, 64, 56],
        [
            [1.00f, 0, 0, 0, 0.12f, 0.04f],
            [0, 1.00f, 0, 0, 0.08f, 0.08f],
            [0, 0, 1.00f, 0, -0.12f, -0.04f],
            [0, 0, 0, 1.00f, -0.08f, -0.08f],
            [0.18f, 0.12f, -0.18f, -0.12f, 0.30f, 0],
            [0.06f, 0.12f, -0.06f, -0.12f, 0, 0.25f]
        ],
        [
            [0, 0.10f, 0, -0.10f, 0.20f, 0.08f],
            [0.10f, 0, -0.10f, 0, 0.15f, 0.20f],
            [0, -0.10f, 0, 0.10f, -0.20f, -0.08f],
            [-0.10f, 0, 0.10f, 0, -0.15f, -0.20f],
            [0.12f, 0.08f, -0.12f, -0.08f, 0.18f, 0.10f],
            [0.08f, 0.12f, -0.08f, -0.12f, 0.10f, 0.18f]
        ],
        [
            [0, 0.06f, 0, -0.06f, 0.08f, 0.12f],
            [0.06f, 0, -0.06f, 0, 0.12f, 0.08f],
            [0, -0.06f, 0, 0.06f, -0.08f, -0.12f],
            [-0.06f, 0, 0.06f, 0, -0.12f, -0.08f],
            [0.05f, 0.08f, -0.05f, -0.08f, 0.10f, 0.06f],
            [0.08f, 0.05f, -0.08f, -0.05f, 0.06f, 0.10f]
        ],
        [7, 10, -7, -10, 5, 6], LearnedPatterns.Square, LearnedPatterns.Near);

    public static byte[] Serialize(NetworkDefinition definition) => JsonSerializer.SerializeToUtf8Bytes(definition,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
}
