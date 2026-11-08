using System.Diagnostics;
using System.Numerics;

namespace Ametista;

public sealed record SearchResult(FastMove Move,int Score,int Depth,long Nodes,long Milliseconds,int Risk,long CacheHits=0,long PreviousTurnCacheHits=0,long EvaluationCacheHits=0,int AspirationRetries=0,long LateReductions=0,long ReductionResearches=0);

// Original implementation for this project. Two neural heads separate position
// value from instability: uncertain positions are protected from late reductions.
public sealed class Search(Network network)
{
    public const string Version="search-v14";
    private const int Mate=30000, MaxPly=96;
    private struct Entry { public ulong Key; public int Score; public uint Move; public short Depth; public byte Bound; public ushort Generation; }
    private readonly Entry[] table=new Entry[1<<20];
    private struct EvaluationEntry { public ulong Key; public Estimate Estimate; public bool Valid; }
    private readonly EvaluationEntry[] evaluations=new EvaluationEntry[1<<18];
    private readonly FastMove[][] moves=Enumerable.Range(0,MaxPly).Select(_=>new FastMove[MoveGenerator.Capacity]).ToArray();
    private readonly int[][] ordering=Enumerable.Range(0,MaxPly).Select(_=>new int[MoveGenerator.Capacity]).ToArray();
    private readonly uint[,] killers=new uint[MaxPly,2];
    private readonly int[,,] history=new int[2,32,32];
    private readonly Dictionary<ulong,int> occurrences=[];
    private readonly ulong[] contextStack=new ulong[MaxPly];
    private ulong contextHash;
    private int contextDepth;
    private readonly Stopwatch timer=new();
    private ushort generation;
    private long nodes;
    private long cacheHits, previousTurnCacheHits;
    private long evaluationCacheHits;
    private int aspirationRetries;
    private long lateReductions,reductionResearches;
    private int budget;
    private CancellationToken cancellation;
    private string? gameId;

    public SearchResult Choose(Board position,string game,int milliseconds,IReadOnlyDictionary<ulong,int> played,CancellationToken token=default,int maximumDepth=80)
    {
        if(gameId!=game) { Array.Clear(table); Array.Clear(evaluations); Array.Clear(history); Array.Clear(killers); gameId=game; }
        if(++generation==0) { Array.Clear(table); generation=1; }
        occurrences.Clear(); foreach(var (key,value) in played) occurrences[key]=value;
        if(!occurrences.ContainsKey(position.Key)) occurrences[position.Key]=1;
        contextHash=0;contextDepth=0;
        foreach(var (key,value) in occurrences)contextHash^=ContextTerm(key,value);
        cacheHits=previousTurnCacheHits=0;
        evaluationCacheHits=0;
        aspirationRetries=0;
        lateReductions=reductionResearches=0;
        cancellation=token; budget=Math.Max(10,milliseconds); nodes=0; timer.Restart();
        int count=MoveGenerator.Generate(position,moves[0]);
        if(count==0) throw new InvalidOperationException("No legal moves.");
        var rootEstimate=Evaluate(position);
        var best=moves[0][0]; int score=rootEstimate.Value, completedDepth=0;
        // Even a single legal move is returned by this engine, not delegated.
        if(count==1) return new(best,score,0,0,timer.ElapsedMilliseconds,rootEstimate.Risk,cacheHits,previousTurnCacheHits,evaluationCacheHits);
        for(int depth=1;depth<=maximumDepth;depth++)
        {
            if(timer.ElapsedMilliseconds>=budget) break;
            try {
                int width=depth>=4?Math.Min(128,32+8*rootEstimate.Risk):Mate;
                var hint=best;
                while(true) {
                    int low=width>=Mate?-Mate:Math.Max(-Mate,score-width);
                    int high=width>=Mate?Mate:Math.Min(Mate,score+width);
                    int alpha=low,beta=high;
                    Order(position,0,count,hint.Code);
                    FastMove iterationBest=hint;int iterationScore=-Mate;
                    for(int i=0;i<count;i++) {
                        CheckTime();SelectNext(0,i,count);
                        var move=moves[0][i];var child=position.Apply(move);
                        int result;
                        Push(child.Key,move.Captures!=0||(position.Kings&(1u<<move.From))==0);
                        try {
                            if(i==0)result=-Negamax(child,depth-1,-beta,-alpha,1,true);
                            else {
                                result=-Negamax(child,depth-1,-alpha-1,-alpha,1,false);
                                if(result>alpha&&result<beta)result=-Negamax(child,depth-1,-beta,-alpha,1,true);
                            }
                        } finally {Pop(child.Key);}
                        if(result>iterationScore){iterationScore=result;iterationBest=move;}
                        alpha=Math.Max(alpha,result);
                        if(alpha>=beta)break;
                    }
                    if(iterationScore<=low||iterationScore>=high) {
                        // Bounds are not completed root evaluations. Re-search
                        // with a wider interval before committing any decision.
                        aspirationRetries++;hint=iterationBest;
                        width=Math.Abs(iterationScore)>Mate-MaxPly?Mate:Math.Min(Mate,width*2);
                        continue;
                    }
                    best=iterationBest;score=iterationScore;completedDepth=depth;
                    break;
                }
                if(Math.Abs(score)>Mate-MaxPly) break;
                // Use the remaining budget; an unfinished iteration never replaces
                // the last fully verified root choice.
            } catch(OperationCanceledException) { break; }
        }
        return new(best,score,completedDepth,nodes,timer.ElapsedMilliseconds,rootEstimate.Risk,cacheHits,previousTurnCacheHits,evaluationCacheHits,aspirationRetries,lateReductions,reductionResearches);
    }

    private int Negamax(Board board,int depth,int alpha,int beta,int ply,bool principal)
    {
        nodes++;
        if((nodes&255)==0) CheckTime();
        var key=board.Key;
        if(ply>=MaxPly-1) {
            // The storage horizon must not hide an already decided result.
            // Referee precedence is no legal move, then draw, then evaluation.
            if(!MoveGenerator.HasCapture(board)&&!MoveGenerator.HasQuiet(board)) return -Mate+ply;
            if(occurrences.GetValueOrDefault(key)>=4||board.Quiet>=80&&(board.White&board.Kings)!=0&&(board.Black&board.Kings)!=0) return 0;
            return Evaluate(board).Value;
        }
        if(depth<=0&&!MoveGenerator.HasCapture(board)) {
            if(!MoveGenerator.HasQuiet(board)) return -Mate+ply;
            if(occurrences.GetValueOrDefault(key)>=4||board.Quiet>=80&&(board.White&board.Kings)!=0&&(board.Black&board.Kings)!=0) return 0;
            return Evaluate(board).Value;
        }
        // Legal terminal state precedes draws, as in the shared referee.
        int count=MoveGenerator.Generate(board,moves[ply]);
        if(count==0) return -Mate+ply;
        if(occurrences.GetValueOrDefault(key)>=4 || board.Quiet>=80&&(board.White&board.Kings)!=0&&(board.Black&board.Kings)!=0) return 0;
        bool captures=moves[ply][0].Captures!=0;
        Estimate estimate=default;
        if(!captures) {
            if(depth<=0) return Evaluate(board).Value;
            // Only the risk head can affect an internal quiet node, and only
            // when late reductions are possible. Avoid unused inference.
            if(!principal&&depth>=4&&count>=5) estimate=Evaluate(board);
        }
        // Mandatory capture sequences are fully resolved beyond the nominal horizon.
        int remaining=Math.Max(depth,0);
        ulong ttKey=key^((ulong)Math.Min(board.Quiet,80)*0xd6e8feb86659fd93UL)^BitOperations.RotateLeft(contextHash,17);
        bool horizonSafe=ply+remaining+BitOperations.PopCount(board.Occupied)<MaxPly-1;
        int slot=(int)(ttKey&(uint)(table.Length-1));
        var entry=table[slot]; uint ttMove=0;
        bool repeated=occurrences.GetValueOrDefault(key)>1;
        if(entry.Key==ttKey) {
            ttMove=entry.Move;
            if(entry.Depth>=remaining&&!principal&&!repeated&&horizonSafe) {
                int saved=FromTable(entry.Score,ply);
                if(entry.Bound==0||entry.Bound==1&&saved>=beta||entry.Bound==2&&saved<=alpha) {
                    cacheHits++;if(entry.Generation!=generation)previousTurnCacheHits++;
                    return saved;
                }
            }
        }
        int originalAlpha=alpha, best=-Mate;
        uint bestMove=0;
        Order(board,ply,count,ttMove);
        for(int i=0;i<count;i++) {
            SelectNext(ply,i,count);
            var move=moves[ply][i]; var next=board.Apply(move);
            // A sole mandatory capture is a forced transition, not a choice.
            // Preserve the decision horizon through it. Every such extension
            // removes a piece, so it cannot create an endless quiet cycle.
            int result, nextDepth=captures&&count==1?remaining:remaining-1;
            Push(next.Key,move.Captures!=0||(board.Kings&(1u<<move.From))==0);
            try {
                if(i==0) result=-Negamax(next,nextDepth,-beta,-alpha,ply+1,principal);
                else {
                    // Risk head guards contact, immobility and unbalanced mobility.
                    int reduction=!principal&&!captures&&!move.Promotes&&remaining>=4&&i>=4&&estimate.Risk<2?1:0;
                    if(reduction>0)lateReductions++;
                    result=-Negamax(next,nextDepth-reduction,-alpha-1,-alpha,ply+1,false);
                    if(reduction>0&&result>alpha) {reductionResearches++;result=-Negamax(next,nextDepth,-alpha-1,-alpha,ply+1,false);}
                    if(result>alpha&&result<beta) result=-Negamax(next,nextDepth,-beta,-alpha,ply+1,principal);
                }
            } finally { Pop(next.Key); }
            if(result>best) { best=result; bestMove=move.Code; }
            alpha=Math.Max(alpha,result);
            if(alpha>=beta) {
                if(!captures) {
                    if(killers[ply,0]!=move.Code) { killers[ply,1]=killers[ply,0]; killers[ply,0]=move.Code; }
                    ref int h=ref history[board.Turn==1?0:1,move.From,move.To];
                    h=Math.Min(20000,h+remaining*remaining);
                }
                break;
            }
        }
        if(!repeated&&horizonSafe&&(entry.Generation!=generation||remaining>=entry.Depth))
            table[slot]=new Entry { Key=ttKey,Score=ToTable(best,ply),Move=bestMove,Depth=(short)remaining,
                Bound=(byte)(best>=beta?1:best<=originalAlpha?2:0),Generation=generation };
        return best;
    }
    private void Order(Board board,int ply,int count,uint best)
    {
        for(int i=0;i<count;i++) {
            var m=moves[ply][i];
            ordering[ply][i]=m.Code==best?1000000 : m.Captures!=0 ? 100000+1000*BitOperations.PopCount(m.Captures)+2000*BitOperations.PopCount(m.Captures&board.Kings)
                : (m.Promotes?80000:0)+(m.Code==killers[ply,0]?50000:m.Code==killers[ply,1]?40000:0)
                  +history[board.Turn==1?0:1,m.From,m.To]+((m.From/4-m.To/4)*board.Turn);
        }
    }
    private void SelectNext(int ply,int first,int count)
    {
        int best=first;
        for(int i=first+1;i<count;i++) if(ordering[ply][i]>ordering[ply][best]) best=i;
        (moves[ply][first],moves[ply][best])=(moves[ply][best],moves[ply][first]);
        (ordering[ply][first],ordering[ply][best])=(ordering[ply][best],ordering[ply][first]);
    }
    private void Push(ulong key,bool irreversible)
    {
        contextStack[contextDepth++]=contextHash;
        int before=occurrences.GetValueOrDefault(key);
        // Earlier positions cannot recur after a capture or a forward man move.
        // Keep referee counts intact, but discard unreachable history from the
        // cache context. Reversible histories are represented as count multisets.
        if(irreversible)contextHash=0;
        else if(before>0)contextHash^=ContextTerm(key,before);
        occurrences[key]=before+1;contextHash^=ContextTerm(key,before+1);
    }
    private void Pop(ulong key) {
        contextHash=contextStack[--contextDepth];
        if(--occurrences[key]==0) occurrences.Remove(key);
    }
    private static ulong ContextTerm(ulong key,int count) {
        ulong value=key^((ulong)count*0x9e3779b97f4a7c15UL);
        value=(value^(value>>30))*0xbf58476d1ce4e5b9UL;
        value=(value^(value>>27))*0x94d049bb133111ebUL;
        return value^(value>>31);
    }
    private Estimate Evaluate(Board board) {
        // Neural inference depends only on piece planes and side to move,
        // unlike search scores, which also depend on history and quiet count.
        ulong key=board.Key;int slot=(int)(key&(uint)(evaluations.Length-1));
        ref var entry=ref evaluations[slot];
        if(entry.Valid&&entry.Key==key){evaluationCacheHits++;return entry.Estimate;}
        var estimate=network.Evaluate(board);
        entry=new EvaluationEntry{Key=key,Estimate=estimate,Valid=true};return estimate;
    }
    private void CheckTime() { if(timer.ElapsedMilliseconds>=budget||cancellation.IsCancellationRequested) throw new OperationCanceledException(); }
    private static int ToTable(int score,int ply)=>score>Mate-MaxPly?score+ply:score< -Mate+MaxPly?score-ply:score;
    private static int FromTable(int score,int ply)=>score>Mate-MaxPly?score-ply:score< -Mate+MaxPly?score+ply:score;
}
