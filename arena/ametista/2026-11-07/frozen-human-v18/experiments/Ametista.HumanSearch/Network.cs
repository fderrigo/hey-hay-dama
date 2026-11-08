using System.Numerics;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace Ametista;

public sealed record Node(string Name,float Bias,int[] From,float[] Weights,float ValueWeight,float RiskWeight);
public sealed record Weights(string Format,string Version,string Construction,int Inputs,Node[] Nodes);
public readonly record struct Estimate(int Value,int Risk);

// The game encoder computes relational input channels, never future positions.
// Inference is only affine transforms, ReLU and two linear output heads.
public sealed class Network
{
    public Weights Definition { get; }
    private readonly InferenceCompiler.Inference? compiled;
    public bool CompiledInference=>compiled is not null;
    private readonly HumanOutcome? human;
    public Network(Weights weights, HumanOutcome? human = null)
    {
        if(weights.Format!="ametista-relu/v1"||weights.Inputs is not(24 or 26)||weights.Nodes.Length is <1 or >128) throw new InvalidDataException("Invalid network.");
        for(int n=0;n<weights.Nodes.Length;n++) {
            var node=weights.Nodes[n];
            if(node.From.Length!=node.Weights.Length||node.From.Any(x=>x<0||x>=weights.Inputs+n)
                ||!float.IsFinite(node.Bias)||!float.IsFinite(node.ValueWeight)||!float.IsFinite(node.RiskWeight)||node.Weights.Any(x=>!float.IsFinite(x)))
                throw new InvalidDataException("Non-feed-forward or invalid network.");
        }
        Definition=weights;
        this.human=human;
        if(RuntimeFeature.IsDynamicCodeSupported)compiled=InferenceCompiler.Compile(weights);
    }
    public static Network Load(string path)=>new(JsonSerializer.Deserialize<Weights>(File.ReadAllText(path),JsonOptions)!);
    public static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true};

    public Estimate Evaluate(Board board,bool scalarEncoder=false,bool interpreted=false)
    {
        Span<float> x=stackalloc float[154];
        var a=scalarEncoder?EncodeScalar(board,board.Turn):Encode(board,board.Turn);
        var b=scalarEncoder?EncodeScalar(board,-board.Turn):Encode(board,-board.Turn);
        x[0]=a.Men-b.Men; x[1]=a.Kings-b.Kings; x[2]=a.Advance-b.Advance;
        x[3]=a.Center-b.Center; x[4]=a.Mobility-b.Mobility; x[5]=a.Blocked-b.Blocked;
        x[6]=a.Support-b.Support; x[7]=a.Isolated-b.Isolated; x[8]=a.Guard-b.Guard;
        x[9]=a.Runner-b.Runner; x[10]=a.KingCenter-b.KingCenter; x[11]=a.Threatened-b.Threatened;
        x[12]=a.Men+b.Men; x[13]=a.Kings+b.Kings; x[14]=a.Mobility; x[15]=b.Mobility;
        x[16]=a.Threatened; x[17]=b.Threatened; x[18]=a.Men; x[19]=b.Men;
        x[20]=a.Kings; x[21]=b.Kings; x[22]=a.Runner; x[23]=b.Runner;
        if(Definition.Inputs==26) {x[24]=KingPressure(board,board.Turn);x[25]=KingPressure(board,-board.Turn);}
        if(compiled is not null&&!interpreted)return AddHuman(board,compiled(x[..Definition.Inputs]));
        float value=0, risk=0;
        for(int n=0;n<Definition.Nodes.Length;n++) {
            var node=Definition.Nodes[n]; float activation=node.Bias;
            for(int j=0;j<node.From.Length;j++) activation+=x[node.From[j]]*node.Weights[j];
            activation=Math.Max(0,activation); x[Definition.Inputs+n]=activation;
            value+=activation*node.ValueWeight; risk+=activation*node.RiskWeight;
        }
        return AddHuman(board,new((int)MathF.Round(value),(int)MathF.Round(risk)));
    }
    private Estimate AddHuman(Board board, Estimate manual)
    {
        if(human is null) return manual;
        // Fixed auxiliary outcome contribution; the manual value/risk graph is intact.
        return new(manual.Value + (int)Math.Round(HumanOutcome.ContributionScale * human.Predict(board)),manual.Risk);
    }
    private static int KingPressure(Board board,int side)
    {
        uint kings=(side==1?board.White:board.Black)&board.Kings;
        if(kings==0)return 0;
        uint targets=side==1?board.Black:board.White;int pressure=0;
        while(targets!=0) {
            int target=BitOperations.TrailingZeroCount(targets);targets&=targets-1;
            int row=target/4,col=row%2+2*(target%4),first=7,second=7;
            uint remaining=kings;
            while(remaining!=0) {
                int square=BitOperations.TrailingZeroCount(remaining);remaining&=remaining-1;
                int r=square/4,c=r%2+2*(square%4);
                int distance=Math.Max(Math.Abs(row-r),Math.Abs(col-c));
                if(distance<first){second=first;first=distance;}
                else if(distance<second)second=distance;
            }
            pressure+=14-first-second;
        }
        return pressure;
    }
    private readonly record struct Features(int Men,int Kings,int Advance,int Center,int Mobility,int Blocked,int Support,int Isolated,int Guard,int Runner,int KingCenter,int Threatened);
    private static bool Runner(Board board,int side,int square)
    {
        uint enemy=side==1?board.Black:board.White;
        var cone=side==1?Geometry.WhiteCone[square]:Geometry.BlackCone[square];
        var intercept=side==1?Geometry.WhiteIntercept[square]:Geometry.BlackIntercept[square];
        return (enemy&cone)==0&&(enemy&board.Kings&intercept)==0;
    }
    private static Features Encode(Board board,int side)
    {
        uint own=side==1?board.White:board.Black, enemy=side==1?board.Black:board.White;
        uint men=own&~board.Kings, kings=own&board.Kings, free=~board.Occupied;
        uint a=own&Geometry.Shift(own,0),b=own&Geometry.Shift(own,1),c=own&Geometry.Shift(own,2),d=own&Geometry.Shift(own,3);
        uint connected=a|b|c|d, doubled=(a&b)|(a&c)|(a&d)|(b&c)|(b&d)|(c&d);
        int support=BitOperations.PopCount(connected)+BitOperations.PopCount(doubled), isolated=BitOperations.PopCount(own&~connected);
        int mobility=0, threatened=0;uint mobile=0;
        for(int direction=0;direction<4;direction++) {
            uint movers=(side==1?direction<2:direction>=2)?own:kings;
            mobility+=BitOperations.PopCount(Geometry.Shift(movers,direction)&free);
            mobile |= movers&Geometry.Shift(free,direction^3);
            uint vulnerable=Geometry.Shift(enemy&board.Kings,direction)&own;
            if(side==1?direction>=2:direction<2) vulnerable |= Geometry.Shift(enemy&~board.Kings,direction)&men;
            threatened+=BitOperations.PopCount(vulnerable&Geometry.Shift(free,direction^3));
        }
        int advance=0, center=0, runner=0, kingCenter=0;
        uint pieces=men;
        while(pieces!=0) {
            int s=BitOperations.TrailingZeroCount(pieces);pieces &= pieces-1;
            int row=s/4,col=row%2+2*(s%4),progress=side==1?7-row:row;
            advance+=progress;if(row is >=2 and <=5&&col is >=2 and <=5)center++;
            if(Runner(board,side,s))runner+=progress*progress;
        }
        pieces=kings;
        while(pieces!=0) {
            int s=BitOperations.TrailingZeroCount(pieces);pieces &= pieces-1;
            int row=s/4,col=row%2+2*(s%4);
            kingCenter+=7-(Math.Abs(2*row-7)+Math.Abs(2*col-7))/2;
        }
        int guard=BitOperations.PopCount(men&(side==1?0xf0000000u:0x0000000fu));
        return new(BitOperations.PopCount(men),BitOperations.PopCount(kings),advance,center,mobility,BitOperations.PopCount(own&~mobile),support,isolated,guard,runner,kingCenter,threatened);
    }
    private static Features EncodeScalar(Board board,int side)
    {
        uint own=side==1?board.White:board.Black, enemy=side==1?board.Black:board.White;
        uint men=own&~board.Kings, kings=own&board.Kings;
        int advance=0, center=0, mobility=0, blocked=0, support=0, isolated=0, guard=0, runner=0, kingCenter=0, threatened=0;
        uint pieces=own;
        while(pieces!=0) {
            int s=BitOperations.TrailingZeroCount(pieces); pieces &= pieces-1;
            bool king=(kings&(1u<<s))!=0;
            int row=s/4, col=row%2+2*(s%4), progress=side==1?7-row:row;
            int localMobility=0;
            for(int d=0;d<4;d++) {
                int step=Geometry.Step[s,d];
                if(step<0) continue;
                if((king||(side==1?d<2:d>=2))&&(board.Occupied&(1u<<step))==0) localMobility++;
                // Is this piece the middle of an opponent's immediate jump?
                int other=Geometry.Step[s,d^3];
                if(other>=0&&(board.Occupied&(1u<<other))==0&&(enemy&(1u<<step))!=0) {
                    bool enemyKing=(board.Kings&(1u<<step))!=0;
                    if(enemyKing||!king&&(side==1?d<2:d>=2)) threatened++;
                }
            }
            mobility+=localMobility;
            if(localMobility==0) blocked++;
            int neighbors=BitOperations.PopCount(Geometry.Neighbors[s]&own);
            if(neighbors==0) isolated++; else support+=Math.Min(neighbors,2);
            if(king) kingCenter+=7-(Math.Abs(2*row-7)+Math.Abs(2*col-7))/2;
            else {
                advance+=progress;
                if(row is >=2 and <=5&&col is >=2 and <=5) center++;
                if(progress==0) guard++;
                if(Runner(board,side,s)) runner+=progress*progress;
            }
        }
        return new(BitOperations.PopCount(men),BitOperations.PopCount(kings),advance,center,mobility,blocked,support,isolated,guard,runner,kingCenter,threatened);
    }

    public static Weights ManualV1()
    {
        var nodes=new List<Node>();
        void Signed(string name,int input,float weight) {
            nodes.Add(new(name+"+",0,[input],[1],weight,0));
            nodes.Add(new(name+"-",0,[input],[-1],-weight,0));
        }
        Signed("men",0,100); Signed("kings",1,265); Signed("advance",2,2);
        Signed("center",3,5); Signed("mobility",4,3); Signed("blocked",5,-5);
        Signed("support",6,2); Signed("isolated",7,-3); Signed("back-guard",8,3);
        Signed("free-promotion-cone",9,1.5f); Signed("king-centrality",10,2); Signed("threatened",11,-8);
        int phase=24+nodes.Count;
        nodes.Add(new("many-men",-6,[12],[1],0,0));
        nodes.Add(new("late-king-centrality+",0,[10,phase],[1,-64],3,0));
        nodes.Add(new("late-king-centrality-",0,[10,phase],[-1,-64],-3,0));
        nodes.Add(new("own-immobility",3,[14],[-1],-15,1));
        nodes.Add(new("enemy-immobility",3,[15],[-1],15,1));
        nodes.Add(new("contact-instability",0,[16,17],[1,1],0,1));
        nodes.Add(new("mobility-imbalance+",-3,[14,15],[1,-1],0,1));
        nodes.Add(new("mobility-imbalance-",-3,[14,15],[-1,1],0,1));
        return new("ametista-relu/v1","manual-v1","Independent hand-written relational channels, ReLU phase gates, value and instability heads. No training, imported weights, external data or automatic tuning.",24,nodes.ToArray());
    }
    public static Weights ManualV2()
    {
        var original=ManualV1();
        var nodes=original.Nodes.Select(n=>n.Name.StartsWith("free-promotion-cone",StringComparison.Ordinal)
            ?n with {ValueWeight=n.ValueWeight>0?3:-3}:n).ToArray();
        return original with {Version="manual-v2",Nodes=nodes,Construction="Manual revision: passed-man channel 3 instead of 1.5, with king interception geometry in the encoder. Motivated by promotion races in development games; no automated fitting or Kingsrow labels."};
    }
    public static Weights ManualV3()
    {
        var original=ManualV2();
        // Up to twelve pieces per side: the opposing king count gates off
        // this term exactly when the opponent also has a king.
        var nodes=original.Nodes.Concat(new Node[]{
            new("exclusive-kings-own",0,[20,21],[1,-12],75,0),
            new("exclusive-kings-enemy",0,[21,20],[1,-12],-75,0)
        }).ToArray();
        return original with {Version="manual-v3",Nodes=nodes,
            Construction="Manual hypothesis: add 75 per king only while the opponent has no king, using two ReLU gates. The hand-chosen bonus is below one man's value and reflects immunity to capture by men. Motivated by development promotion phases and discussion, not fitted to outcomes or copied from another engine."};
    }
    public static Weights ManualV4()
    {
        var original=ManualV3();
        // Two new inputs precede the hidden nodes. Preserve every old edge,
        // including phase-node references, by shifting hidden indices only.
        var nodes=original.Nodes.Select(n=>n with {From=n.From.Select(i=>i<24?i:i+2).ToArray()}).ToList();
        // Pressure <= 12*14 < 256. Subtracting the second ReLU removes the
        // gate's constant offset: this adds only pressure, not another king bonus.
        nodes.Add(new("king-majority-pressure-own",-256,[24,20,21],[1,256,-256],0.5f,0));
        nodes.Add(new("king-majority-offset-own",-256,[20,21],[256,-256],-0.5f,0));
        nodes.Add(new("king-majority-pressure-enemy",-256,[25,21,20],[1,256,-256],-0.5f,0));
        nodes.Add(new("king-majority-offset-enemy",-256,[21,20],[256,-256],0.5f,0));
        return original with {Version="manual-v4",Inputs=26,Nodes=nodes.ToArray(),
            Construction="Original manual conversion hypothesis from our forty-move draw replay: two-nearest-king geometric pressure, gated by king majority. Hand coefficient 0.5, bounded below one man for legal boards; all prior edges preserved. No outcome fitting, imported tables or opponent evaluation labels. Geometry ignores blockers and does not assert a won ending."};
    }
}
