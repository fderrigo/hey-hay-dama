using System.Numerics;
using Dama.Engine;

namespace Ametista;

public readonly record struct Board(uint White, uint Black, uint Kings, int Turn, int Quiet)
{
    public uint Occupied => White | Black;
    public uint Own => Turn == 1 ? White : Black;
    public uint Enemy => Turn == 1 ? Black : White;
    public static Board From(Position p)
    {
        uint white=0, black=0, kings=0;
        for(int i=0;i<32;i++) {
            if(p.Board[i]>0) white |= 1u<<i;
            if(p.Board[i]<0) black |= 1u<<i;
            if(Math.Abs(p.Board[i])==2) kings |= 1u<<i;
        }
        return new(white,black,kings,p.Turn,p.QuietKingPlyCount);
    }
    public Position ToPosition(int ply=0)
    {
        var pieces=new int[32];
        for(int i=0;i<32;i++) if((Occupied&(1u<<i))!=0)
            pieces[i]=((White&(1u<<i))!=0?1:-1)*((Kings&(1u<<i))!=0?2:1);
        return new(pieces,Turn,ply,Quiet);
    }
    public Board Apply(FastMove move)
    {
        uint from=1u<<move.From, to=1u<<move.To;
        var king=(Kings&from)!=0;
        var white=Turn==1 ? (White&~from)|to : White&~move.Captures;
        var black=Turn==-1 ? (Black&~from)|to : Black&~move.Captures;
        var kings=Kings&~(from|move.Captures);
        if(king||move.Promotes) kings |= to;
        return new(white,black,kings,-Turn,king&&move.Captures==0?Quiet+1:0);
    }
    public ulong Key
    {
        get {
            // Mix independent bit planes; this is a search key, not a learned feature.
            ulong x=((ulong)White<<32)|Black;
            x ^= (ulong)Kings*0x9e3779b97f4a7c15UL;
            x ^= Turn==1?0x243f6a8885a308d3UL:0x13198a2e03707344UL;
            x ^= x>>30; x *= 0xbf58476d1ce4e5b9UL;
            x ^= x>>27; x *= 0x94d049bb133111ebUL;
            return x^(x>>31);
        }
    }
}

public readonly record struct FastMove(int From,int To,uint Captures,bool Promotes)
{
    public uint Code => ((uint)From|((uint)To<<5)) ^ BitOperations.RotateLeft(Captures,11);
}

public static class Geometry
{
    public static uint Shift(uint pieces,int direction) => direction switch {
        0 => ((pieces&0x0e0e0e0eu)>>5)|((pieces&0xf0f0f0f0u)>>4),
        1 => ((pieces&0x0f0f0f0fu)>>4)|((pieces&0x70707070u)>>3),
        2 => ((pieces&0x0e0e0e0eu)<<3)|((pieces&0xf0f0f0f0u)<<4),
        _ => ((pieces&0x0f0f0f0fu)<<4)|((pieces&0x70707070u)<<5)
    };
    public static readonly int[,] Step=new int[32,4];
    public static readonly int[,] Jump=new int[32,4];
    public static readonly uint[] Neighbors=new uint[32];
    public static readonly uint[] WhiteCone=new uint[32];
    public static readonly uint[] BlackCone=new uint[32];
    public static readonly uint[] WhiteIntercept=new uint[32];
    public static readonly uint[] BlackIntercept=new uint[32];
    static Geometry()
    {
        for(int s=0;s<32;s++) {
            var (r,c)=Rules.Coordinates(s);
            for(int d=0;d<4;d++) {
                int dr=d<2?-1:1, dc=d%2==0?-1:1;
                Step[s,d]=Rules.Square(r+dr,c+dc); Jump[s,d]=Rules.Square(r+2*dr,c+2*dc);
                if(Step[s,d]>=0) Neighbors[s] |= 1u<<Step[s,d];
            }
            for(int t=0;t<32;t++) {
                var (tr,tc)=Rules.Coordinates(t);
                if(Math.Abs(tc-c)<=Math.Abs(tr-r)+1) {
                    if(tr<r) WhiteCone[s] |= 1u<<t;
                    if(tr>r) BlackCone[s] |= 1u<<t;
                }
                for(int goal=0;goal<8;goal+=2) {
                    if(Math.Abs(goal-c)<=r && Math.Max(tr,Math.Abs(tc-goal))<=r+1) WhiteIntercept[s] |= 1u<<t;
                    if(Math.Abs(goal+1-c)<=7-r && Math.Max(7-tr,Math.Abs(tc-goal-1))<=8-r) BlackIntercept[s] |= 1u<<t;
                }
            }
        }
    }
}

public static class MoveGenerator
{
    public const int Capacity=256;
    public static bool HasCapture(Board board)
    {
        uint ownKings=board.Own&board.Kings, ownMen=board.Own&~board.Kings;
        uint enemy=board.Enemy, enemyMen=enemy&~board.Kings, free=~board.Occupied;
        for(int d=0;d<4;d++) {
            uint victims=Geometry.Shift(ownKings,d)&enemy;
            if(board.Turn==1?d<2:d>=2) victims |= Geometry.Shift(ownMen,d)&enemyMen;
            if((Geometry.Shift(victims,d)&free)!=0) return true;
        }
        return false;
    }
    public static bool HasQuiet(Board board)
    {
        uint kings=board.Own&board.Kings, free=~board.Occupied;
        for(int d=0;d<4;d++) if((Geometry.Shift((board.Turn==1?d<2:d>=2)?board.Own:kings,d)&free)!=0) return true;
        return false;
    }
    public static int Generate(Board board, Span<FastMove> moves)
    {
        int count=0, bestPriority=-1;
        uint pieces=board.Own;
        if(!HasCapture(board)) pieces=0;
        while(pieces!=0) {
            int from=BitOperations.TrailingZeroCount(pieces); pieces &= pieces-1;
            bool king=(board.Kings&(1u<<from))!=0;
            Capture(board,from,from,king,0,0,0,0,moves,ref count,ref bestPriority);
        }
        if(count>0) return count;
        pieces=board.Own;
        while(pieces!=0) {
            int from=BitOperations.TrailingZeroCount(pieces); pieces &= pieces-1;
            bool king=(board.Kings&(1u<<from))!=0;
            for(int d=0;d<4;d++) {
                if(!king && (board.Turn==1 ? d>=2 : d<2)) continue;
                int to=Geometry.Step[from,d];
                if(to<0||(board.Occupied&(1u<<to))!=0) continue;
                if(count==moves.Length) throw new InvalidOperationException("Move capacity exceeded.");
                moves[count++]=new(from,to,0,!king&&Promotes(board.Turn,to));
            }
        }
        return count;
    }

    private static void Capture(Board board,int start,int at,bool king,uint taken,int length,int kingsTaken,int order,
        Span<FastMove> moves,ref int count,ref int bestPriority)
    {
        bool extended=false;
        uint occupied=(board.Occupied&~(1u<<start))|(1u<<at);
        uint victims=board.Enemy&~taken;
        if(!king) victims &= ~board.Kings;
        for(int d=0;d<4;d++) {
            if(!king&&(board.Turn==1?d>=2:d<2)) continue;
            int over=Geometry.Step[at,d], to=Geometry.Jump[at,d];
            if(to<0||over<0||(victims&(1u<<over))==0||(occupied&(1u<<to))!=0) continue;
            extended=true;
            uint captured=taken|(1u<<over);
            int rank=(board.Kings&(1u<<over))!=0?1:0;
            if(!king&&Promotes(board.Turn,to))
                Save(start,to,captured,true,length+1,false,kingsTaken+rank,(order<<1)|rank,moves,ref count,ref bestPriority);
            else Capture(board,start,to,king,captured,length+1,kingsTaken+rank,(order<<1)|rank,moves,ref count,ref bestPriority);
        }
        if(!extended&&length>0) Save(start,at,taken,false,length,king,kingsTaken,order,moves,ref count,ref bestPriority);
    }
    private static bool Promotes(int turn,int square) => turn==1?square<4:square>=28;
    private static void Save(int from,int to,uint captures,bool promotes,int length,bool king,int kingsTaken,int order,
        Span<FastMove> moves,ref int count,ref int bestPriority)
    {
        int priority=(length<<20)|(king?1<<19:0)|(kingsTaken<<12)|order;
        if(priority<bestPriority) return;
        if(priority>bestPriority) { count=0; bestPriority=priority; }
        var move=new FastMove(from,to,captures,promotes);
        // Different legal paths can yield the same position; search it only once.
        for(int i=0;i<count;i++) if(moves[i]==move) return;
        if(count==moves.Length) throw new InvalidOperationException("Move capacity exceeded.");
        moves[count++]=move;
    }
}
