using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Dama.Engine;

namespace Kingsrow;

// CheckerBoard's Italian board uses column-major [x][y], with row 0 at our top.
// This adapter does not implement a second rules engine: it matches the returned
// board against complete legal transitions from the shared referee.
public sealed class NativeEngine
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int Command(string command, StringBuilder reply);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int GetMove([In, Out] int[] board, int color, double seconds,
        StringBuilder reply, ref int playNow, int info, int moreInfo, IntPtr move);
    private readonly Command command;
    private readonly GetMove getMove;
    private readonly object gate = new();
    public string Name { get; }
    public string Sha256 { get; }

    public NativeEngine(string dll)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Kingsrow Italian richiede Windows x64.");
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dll)));
        // Native engine resources are relative to the DLL installation directory.
        Directory.SetCurrentDirectory(Path.GetDirectoryName(dll)!);
        var library = NativeLibrary.Load(dll);
        command = Marshal.GetDelegateForFunctionPointer<Command>(NativeLibrary.GetExport(library, "enginecommand"));
        getMove = Marshal.GetDelegateForFunctionPointer<GetMove>(NativeLibrary.GetExport(library, "getmove"));
        Name = Send("name");
        if (Send("get gametype").Trim() != "22") throw new InvalidOperationException("La DLL non dichiara dama italiana (gametype 22).");
        Send("set hashsize 64");
    }

    private string Send(string text)
    {
        var reply = new StringBuilder(1024);
        command(text, reply);
        return reply.ToString();
    }

    public static int[] Encode(int[] board)
    {
        var result = new int[64];
        for (var s = 0; s < 32; s++)
        {
            var (r, c) = Rules.Coordinates(s);
            result[c * 8 + r] = board[s] == 0 ? 0 : (board[s] > 0 ? 1 : 2) + (Math.Abs(board[s]) == 1 ? 4 : 8);
        }
        return result;
    }

    public Move Choose(Position position, List<Move> legal, double seconds)
    {
        lock (gate)
        {
            var board = Encode(position.Board);
            var output = new StringBuilder(1024);
            var playNow = 0;
            var move = Marshal.AllocHGlobal(1024);
            try
            {
                // New search context on every request: no shared game history leaks
                // between rooms. Repetition remains the application's responsibility.
                getMove(board, position.Turn == 1 ? 1 : 2, seconds, output, ref playNow, 3, 0, move);
                return legal.FirstOrDefault(m => Encode(Rules.Apply(position, m).Board).SequenceEqual(board))
                    ?? throw new InvalidOperationException("Kingsrow ha restituito una transizione incompatibile con l'arbitro.");
            }
            finally { Marshal.FreeHGlobal(move); }
        }
    }
}
