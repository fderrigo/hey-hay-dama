using Dama.Engine;

namespace Ametista;

internal static class HumanVerification
{
    public static object Run()
    {
        int checks=0;
        void Check(bool ok) {if(!ok)throw new InvalidDataException("Synthetic human-combination verification failed.");checks++;}
        var zeroDefinition=new HumanWeights("ametista-human-linear/v1","ametista-human-linear-v1","zero",128,"tanh",1/Math.Sqrt(24),new double[128],0,100);
        var zero=new HumanOutcome(zeroDefinition);
        var manual=new Network(Network.ManualV4());
        var combinedZero=new Network(Network.ManualV4(),zero);
        var positions=new[]{Board.From(Position.Initial()),new Board(1u<<4,1u<<27,(1u<<4)|(1u<<27),1,2),new Board(1u<<21,1u<<17,0,-1,0)};
        foreach(var board in positions) {
            Check(zero.Predict(board)==0);
            Check(manual.Evaluate(board)==combinedZero.Evaluate(board));
            Check(manual.Evaluate(board,interpreted:true)==combinedZero.Evaluate(board,interpreted:true));
        }
        var coefficients=Enumerable.Range(0,128).Select(i=>(i-64)*.003).ToArray();
        var human=new HumanOutcome(zeroDefinition with {Weights=coefficients,Bias=.037});
        var combined=new Network(Network.ManualV4(),human);
        foreach(var board in positions) {
            var position=board.ToPosition();
            var transformed=Board.From(new Position(position.Board.Reverse().Select(p=>-p).ToArray(),-position.Turn));
            Check(human.Predict(board)==human.Predict(transformed));
            double reference=.037;
            var canonical=position.Turn==1?position.Board:position.Board.Reverse().Select(p=>-p).ToArray();
            var planes=new[]{1,2,-1,-2};
            for(int plane=0;plane<4;plane++)for(int s=0;s<32;s++)
                if(canonical[s]==planes[plane])reference+=coefficients[plane*32+s]/Math.Sqrt(24);
            Check(Math.Abs(human.Predict(board)-Math.Tanh(reference))<1e-14);
            var baseline=manual.Evaluate(board);var result=combined.Evaluate(board);
            Check(result.Risk==baseline.Risk);
            Check(Math.Abs(result.Value-baseline.Value)<=100);
            Check(result.Value==baseline.Value+(int)Math.Round(100*human.Predict(board)));
            Check(result==combined.Evaluate(board,interpreted:true));
        }
        foreach(double bias in new[]{-1000.0,1000.0}) {
            var saturated=new Network(Network.ManualV4(),new HumanOutcome(zeroDefinition with {Bias=bias}));
            var b=positions[0];Check(saturated.Evaluate(b).Value-manual.Evaluate(b).Value==Math.Sign(bias)*100);
            Check(saturated.Evaluate(b).Risk==manual.Evaluate(b).Risk);
        }
        bool rejected=false;
        try {_=new HumanOutcome(zeroDefinition with {Weights=new double[127]});}catch(InvalidDataException){rejected=true;}
        Check(rejected);
        rejected=false;
        try {_=new HumanOutcome(zeroDefinition with {Bias=double.NaN});}catch(InvalidDataException){rejected=true;}
        Check(rejected);
        rejected=false;
        try {_=new HumanOutcome(zeroDefinition with {FeatureScale=1});}catch(InvalidDataException){rejected=true;}
        Check(rejected);
        return new {passed=true,checks,scale=HumanOutcome.ContributionScale,manualZeroRecovery=true,perspectiveEquivalent=true,riskUnchanged=true,realGames=0,training=false};
    }
}
