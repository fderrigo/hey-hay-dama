using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ametista;

public sealed record HumanWeights(string Format,string Name,string Initialization,int Inputs,string Activation,
    double FeatureScale,double[] Weights,double Bias,int TrainingEpochs);

// Only the separately trained, original 128-input neuron is loaded here.
// No imported engine evaluator, source code, tablebase or pretrained weights.
public sealed class HumanOutcome
{
    public const double ContributionScale=100;
    private const string Format="ametista-human-linear/v1";
    public HumanWeights Definition {get;}
    public string Sha256 {get;}
    private readonly double[] weights;
    private readonly double bias;
    private readonly double featureScale;

    public HumanOutcome(HumanWeights definition,string sha256="synthetic-fixture")
    {
        if(definition.Format!=Format || definition.Name!="ametista-human-linear-v1" || definition.Initialization!="zero"
            || definition.Inputs!=128 || definition.Activation!="tanh" || definition.TrainingEpochs!=100
            || definition.Weights is not {Length:128} || definition.Weights.Any(x=>!double.IsFinite(x))
            || !double.IsFinite(definition.Bias) || !double.IsFinite(definition.FeatureScale)
            || Math.Abs(definition.FeatureScale-1/Math.Sqrt(24))>1e-15)
            throw new InvalidDataException("Invalid original human-linear/v1 model.");
        Definition=definition;Sha256=sha256;
        weights=(double[])definition.Weights.Clone();bias=definition.Bias;featureScale=definition.FeatureScale;
    }

    public static HumanOutcome Load(string path)
    {
        byte[] bytes=File.ReadAllBytes(path);
        using var document=JsonDocument.Parse(bytes);
        var required=new HashSet<string>(new[]{"format","name","initialization","inputs","activation","featureScale","weights","bias","trainingEpochs"},StringComparer.Ordinal);
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Expected model object.");
        foreach(var property in document.RootElement.EnumerateObject())
            if(!required.Remove(property.Name))throw new InvalidDataException("Duplicate or unexpected human model property.");
        if(required.Count!=0)throw new InvalidDataException("Missing human model fields.");
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
        var definition=JsonSerializer.Deserialize<HumanWeights>(bytes,options) ?? throw new InvalidDataException("Missing human model.");
        return new(definition,Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public double Predict(Board board)
    {
        double sum=bias;
        // Match Python encode: black rotates 180 degrees and swaps colors.
        // Four 32-square planes, each occupied feature divided by sqrt(24).
        for(int encodedSquare=0;encodedSquare<32;encodedSquare++) {
            int sourceSquare=board.Turn==1?encodedSquare:31-encodedSquare;
            uint bit=1u<<sourceSquare;
            if((board.Occupied&bit)==0)continue;
            bool white=(board.White&bit)!=0;
            bool own=board.Turn==1?white:!white;
            bool king=(board.Kings&bit)!=0;
            int plane=(own?0:2)+(king?1:0);
            sum+=weights[plane*32+encodedSquare]*featureScale;
        }
        if(!double.IsFinite(sum))throw new InvalidDataException("Human prediction overflow.");
        return Math.Tanh(sum);
    }
}
