using System.Text.Json;

namespace Diaspro;

public sealed record NetworkDefinition(string Format, string Name, string Construction, string[] Features, float[] Coefficients,
    float[][] GraphSelf, float[][] GraphNear, float[][] GraphJump, float[] GraphHead);

public static class ManualWeights
{
    public const string Version = "diaspro-v3";
    public static readonly string[] FeatureNames = [
        "bias", "material", "advancement", "promotion-race", "back-rank", "centrality", "king-centrality",
        "quiet-mobility", "safe-mobility", "immediate-capture", "potential-capture", "capture-promotion",
        "exposed-material", "supported-material", "edge-men", "crowding", "quiet-king-pressure",
        "undefended-exposure", "continued-capture"
    ];

    public static NetworkDefinition Build() => new("diaspro-relational/v1", Version,
        "Coefficienti manuali; due propagazioni relazionali; geometria statica delle riprese; nessun training, ricerca o dato esterno.",
        FeatureNames,
        [0, 112, 5, 8, 4, 3, 5, 5, 8, 0.92f, 0.18f, 0.72f, 24, 4, -2, -3, 0.35f, 36, 56],
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
        [7, 10, -7, -10, 5, 6]);

    public static byte[] Serialize(NetworkDefinition definition) => JsonSerializer.SerializeToUtf8Bytes(definition,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
}
