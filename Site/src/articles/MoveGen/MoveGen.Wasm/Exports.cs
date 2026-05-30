using System.Runtime.InteropServices.JavaScript;
using System.Text;
using MoveGen.App;

namespace MoveGen.Wasm;

public static partial class Exports
{
    /// <summary>Returns the perft node count from <paramref name="fen"/> at <paramref name="depth"/>.
    /// Uses double so the value marshals directly to a JS Number — exact up to 2^53 which is plenty for any perft we'd run in a browser.</summary>
    [JSExport]
    public static double Perft(string fen, int depth)
    {
        if (depth < 0) return 0;
        Position pos;
        try { pos = Fen.Parse(fen); }
        catch { return -1; }
        return (double)MoveGen.App.Perft.Run(pos, depth);
    }

    /// <summary>
    /// Per-root-move perft (depth − 1 from each root). Returns "uci count\n…" lines so the JS side
    /// can parse without us shipping a JSON serializer. Empty string on error.
    /// </summary>
    [JSExport]
    public static string PerftDivide(string fen, int depth)
    {
        if (depth < 1) return string.Empty;
        Position pos;
        try { pos = Fen.Parse(fen); }
        catch { return string.Empty; }

        Span<Move> buf = stackalloc Move[256];
        int n = LegalMoveGenerator.Generate(pos, buf);

        var sb = new StringBuilder(n * 16);
        for (int i = 0; i < n; i++)
        {
            var m = buf[i];
            pos.MakeMove(m);
            ulong sub = depth == 1 ? 1UL : MoveGen.App.Perft.Run(pos, depth - 1);
            pos.UnmakeMove(m);
            if (i > 0) sb.Append('\n');
            sb.Append(m.ToUci()).Append(' ').Append(sub);
        }
        return sb.ToString();
    }

    /// <summary>Identifying string so JS can confirm which engine is active.</summary>
    [JSExport]
    public static string EngineInfo() =>
        $"MoveGen.Wasm (.NET {Environment.Version}, AOT)";

    /// <summary>Space-separated UCI list of legal moves from <paramref name="fen"/>. Empty string on parse error.</summary>
    [JSExport]
    public static string LegalMovesUci(string fen)
    {
        Position pos;
        try { pos = Fen.Parse(fen); } catch { return string.Empty; }
        Span<Move> buf = stackalloc Move[256];
        int n = LegalMoveGenerator.Generate(pos, buf);
        var sb = new StringBuilder(n * 6);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(buf[i].ToUci());
        }
        return sb.ToString();
    }

    /// <summary>Applies <paramref name="uci"/> from <paramref name="fen"/> and returns perft(depth) from the resulting position.
    /// Lets JS iterate root moves and surface progress between them without losing native perft speed inside each subtree.
    /// Returns -1 if the move isn't legal from the FEN.</summary>
    [JSExport]
    public static double PerftRootMove(string fen, string uci, int depth)
    {
        Position pos;
        try { pos = Fen.Parse(fen); } catch { return -1; }
        Span<Move> buf = stackalloc Move[256];
        int n = LegalMoveGenerator.Generate(pos, buf);
        for (int i = 0; i < n; i++)
        {
            if (buf[i].ToUci() == uci)
            {
                pos.MakeMove(buf[i]);
                double sub = depth <= 0 ? 1.0 : (double)MoveGen.App.Perft.Run(pos, depth);
                pos.UnmakeMove(buf[i]);
                return sub;
            }
        }
        return -1;
    }
}
